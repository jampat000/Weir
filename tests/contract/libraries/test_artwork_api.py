"""Posters: a file's title is looked up once through the metadata service, the image is served from Weir, and the
Artwork switch turns it all off. The metadata service is a fake; no test reaches the real one."""

from __future__ import annotations

from collections.abc import Callable
from typing import Any

import pytest

from tests.contract.libraries import _helpers as h
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.fake_gateway import IMAGE_BYTES, FakeGateway
from tests.contract.support.launcher import ServerUnderTest
from tests.contract.support.polling import never_within, wait_until

METADATA = f"{API}/processing/metadata-provider"
POSTER_PREFIX = f"{API}/artwork/posters/"
#: The resolver wakes every five seconds; this is long enough for it to have woken twice.
TWO_PASSES_S = 11.0


@pytest.fixture
def start(server_factory: Callable[..., ServerUnderTest], fake_gateway: FakeGateway) -> Callable[[], ServerUnderTest]:
    """A fresh server pointed at the fake gateway."""

    return lambda: server_factory(env={"WEIR_ARTWORK_GATEWAY_URL": fake_gateway.base_url})


def seed_file(server: ServerUnderTest, path: str, *, media_type: str = "movie", activity: bool = False) -> None:
    """A file Weir knows (and, when asked, an Activity entry about it), written while the server is stopped."""

    with seed.stopped(server) as conn:
        library_id = seed.scalar(
            conn, "SELECT id FROM libraries WHERE media_type = ? ORDER BY id LIMIT 1", (media_type,)
        )
        conn.execute(
            "INSERT INTO files (library_id, relative_path, status, last_seen_at) VALUES (?, ?, 'processed', ?)",
            (library_id, path, seed.utc_text()),
        )
        if activity:
            conn.execute(
                "INSERT INTO activity_events (event_type, module, title, library_id, relative_path, created_at) "
                "VALUES ('processing.file_remux_pass_completed', 'processing', 'Finished', ?, ?, ?)",
                (library_id, path, seed.utc_text()),
            )


def poster_url_of(client: WeirClient, path: str) -> str | None:
    listed = client.get(f"{API}/processing/files")
    assert listed.status_code == 200, listed.text
    for entry in listed.json()["files"]:
        if entry["relative_path"] == path:
            assert "poster_url" in entry, entry
            return entry["poster_url"]
    raise AssertionError(f"{path} is not in the files list")


def wait_for_poster(client: WeirClient, path: str) -> str:
    return wait_until(lambda: poster_url_of(client, path), what=f"a poster address for {path}")


def test_a_file_shows_the_poster_of_its_title_once_the_title_is_looked_up(
    start: Callable[[], ServerUnderTest], fake_gateway: FakeGateway, client_factory: Callable[..., WeirClient]
) -> None:
    fake_gateway.knows("nosferatu", "nosferatu.jpg", tmdb_id=653)
    server = start()
    path = "Nosferatu (1922)/Nosferatu.1922.1080p.mkv"
    seed_file(server, path, activity=True)
    admin = h.signed_in_admin(server, client_factory)

    url = wait_for_poster(admin, path)

    assert url.startswith(POSTER_PREFIX)
    search = fake_gateway.searches()[0]
    assert search.query == {"mediaType": ["movies"], "query": ["nosferatu"], "year": ["1922"]}
    image = admin.get(url)
    assert image.status_code == 200
    assert image.headers["content-type"] == "image/jpeg"
    cache = image.headers["cache-control"].replace(" ", "").split(",")
    assert {"private", "max-age=2592000", "immutable"} <= set(cache)
    assert image.content == IMAGE_BYTES
    assert [r.path for r in fake_gateway.image_requests()] == ["/artwork/w342/nosferatu.jpg"]
    entries = admin.get(f"{API}/activity/recent", params={"module": "processing"}).json()["items"]
    assert [entry["poster_url"] for entry in entries] == [url]


def test_a_poster_needs_a_signed_in_user_and_an_unknown_id_is_a_404(
    start: Callable[[], ServerUnderTest], fake_gateway: FakeGateway, client_factory: Callable[..., WeirClient]
) -> None:
    fake_gateway.knows("nosferatu", "nosferatu.jpg")
    server = start()
    path = "Nosferatu (1922)/Nosferatu.1922.1080p.mkv"
    seed_file(server, path)
    admin = h.signed_in_admin(server, client_factory)
    url = wait_for_poster(admin, path)

    assert client_factory(server).get(url).status_code == 401
    assert admin.get(f"{POSTER_PREFIX}0123456789abcdef0123456789abcdef").status_code == 404
    assert admin.get(f"{POSTER_PREFIX}not-a-poster").status_code == 404


def test_two_files_of_one_title_are_looked_up_once(
    start: Callable[[], ServerUnderTest], fake_gateway: FakeGateway, client_factory: Callable[..., WeirClient]
) -> None:
    fake_gateway.knows("metropolis", "metropolis.jpg")
    server = start()
    paths = ["Metropolis (1927)/Metropolis.1927.1080p.mkv", "Metropolis (1927)/Metropolis.1927.extras.mkv"]
    for path in paths:
        seed_file(server, path)
    admin = h.signed_in_admin(server, client_factory)

    urls = [wait_for_poster(admin, path) for path in paths]

    assert urls[0] == urls[1]
    assert len(fake_gateway.searches_for("metropolis")) == 1


def test_every_episode_of_a_series_shares_one_lookup_of_the_series(
    start: Callable[[], ServerUnderTest], fake_gateway: FakeGateway, client_factory: Callable[..., WeirClient]
) -> None:
    fake_gateway.knows("example show", "show.jpg")
    server = start()
    paths = ["Example Show/Season 1/Example.Show.S01E01.mkv", "Example Show/Season 1/Example.Show.S01E02.mkv"]
    for path in paths:
        seed_file(server, path, media_type="tv")
    admin = h.signed_in_admin(server, client_factory)

    for path in paths:
        wait_for_poster(admin, path)

    searches = fake_gateway.searches_for("example show")
    assert len(searches) == 1
    assert searches[0].query["mediaType"] == ["tv"]


def test_a_title_the_service_does_not_know_has_no_poster_and_is_not_asked_about_again(
    start: Callable[[], ServerUnderTest], fake_gateway: FakeGateway, client_factory: Callable[..., WeirClient]
) -> None:
    server = start()
    path = "Zzzq Unknown (2001)/Zzzq.Unknown.2001.mkv"
    seed_file(server, path)
    admin = h.signed_in_admin(server, client_factory)

    wait_until(lambda: fake_gateway.searches_for("zzzq unknown"), what="the title to be searched")

    never_within(
        lambda: len(fake_gateway.searches_for("zzzq unknown")) > 1,
        seconds=TWO_PASSES_S,
        what="a second search for a title the service does not know",
    )
    assert poster_url_of(admin, path) is None


def test_a_busy_service_is_left_alone_for_as_long_as_it_asked(
    start: Callable[[], ServerUnderTest], fake_gateway: FakeGateway, client_factory: Callable[..., WeirClient]
) -> None:
    fake_gateway.busy(retry_after_s=600)
    server = start()
    for path in ["First Film (2010)/First.Film.2010.mkv", "Second Film (2011)/Second.Film.2011.mkv"]:
        seed_file(server, path)
    admin = h.signed_in_admin(server, client_factory)

    wait_until(lambda: fake_gateway.searches(), what="the first search")

    never_within(
        lambda: len(fake_gateway.searches()) > 1,
        seconds=TWO_PASSES_S,
        what="another search while the service asked Weir to wait",
    )
    assert poster_url_of(admin, "First Film (2010)/First.Film.2010.mkv") is None


def test_switching_artwork_off_hides_posters_and_stops_lookups_and_on_brings_them_back(
    start: Callable[[], ServerUnderTest], fake_gateway: FakeGateway, client_factory: Callable[..., WeirClient]
) -> None:
    fake_gateway.knows("nosferatu", "nosferatu.jpg")
    fake_gateway.knows("metropolis", "metropolis.jpg")
    server = start()
    first = "Nosferatu (1922)/Nosferatu.1922.1080p.mkv"
    seed_file(server, first)
    admin = h.signed_in_admin(server, client_factory)
    wait_for_poster(admin, first)

    off = admin.put_csrf(METADATA, {"provider": "", "artwork_enabled": False})
    assert off.status_code == 200, off.text
    assert off.json()["artwork_enabled"] is False
    assert poster_url_of(admin, first) is None

    second = "Metropolis (1927)/Metropolis.1927.1080p.mkv"
    seed_file(server, second)
    admin = h.signed_in_admin(server, client_factory)
    never_within(
        lambda: fake_gateway.searches_for("metropolis"),
        seconds=TWO_PASSES_S,
        what="a lookup while Artwork is off",
    )
    assert poster_url_of(admin, second) is None

    on = admin.put_csrf(METADATA, {"provider": "", "artwork_enabled": True})
    assert on.status_code == 200, on.text
    assert wait_for_poster(admin, first)
    assert wait_for_poster(admin, second)


def test_the_files_list_always_names_the_poster_field(
    server: ServerUnderTest, client_factory: Callable[..., WeirClient]
) -> None:
    """With posters off for the whole server, every file still carries ``poster_url``, as null."""

    seed_file(server, "Plain (2000)/Plain.2000.mkv")
    admin: Any = h.signed_in_admin(server, client_factory)

    assert poster_url_of(admin, "Plain (2000)/Plain.2000.mkv") is None
