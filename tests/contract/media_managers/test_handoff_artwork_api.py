"""Deluno's hand-off carries optional title fields for posters. Weir uses the ids and the poster address when they are
there, ignores a malformed field or a poster address on another host, and still accepts a hand-off with none of them.
The metadata service is a fake; no test reaches the real one."""

from __future__ import annotations

from collections.abc import Callable
from pathlib import Path
from typing import Any

import httpx
import pytest

from tests.contract.media_managers._handoff_status_helpers import SECRET, SECRET_ENV
from tests.contract.media_managers._helpers import CALLBACK_PATH, LibraryFolders, ensure_library
from tests.contract.support.client import API, WeirClient
from tests.contract.support.fake_gateway import FakeGateway
from tests.contract.support.launcher import ServerUnderTest
from tests.contract.support.polling import never_within, wait_until


@pytest.fixture
def started(
    server_factory: Callable[..., ServerUnderTest],
    fake_gateway: FakeGateway,
    client_factory: Callable[..., WeirClient],
    tmp_path: Path,
) -> tuple[ServerUnderTest, WeirClient, LibraryFolders]:
    """A server pointed at the fake gateway, with a Movies library that watches a folder of this test's."""

    server = server_factory(env={**SECRET_ENV, "WEIR_ARTWORK_GATEWAY_URL": fake_gateway.base_url})
    admin = client_factory(server)
    admin.ensure_admin()
    folders = LibraryFolders.make(tmp_path / "movies")
    ensure_library(admin, name="Movies", media_type="movie", folders=folders)
    return server, admin, folders


def hand_off(server: ServerUnderTest, folders: LibraryFolders, **extra: Any) -> httpx.Response:
    source = folders.watched / "Film" / "film.mkv"
    source.parent.mkdir(parents=True, exist_ok=True)
    source.write_bytes(b"x")
    body = {
        "eventType": "deluno.processor-handoff",
        "handoffId": "h-poster",
        "libraryId": "lib-1",
        "mediaType": "movies",
        "sourcePath": str(source),
        "releaseName": "Metropolis.1927.1080p.BluRay-GRP",
        "callbackPath": CALLBACK_PATH,
        **extra,
    }
    return httpx.post(f"{server.base_url}{API}/intake/webhook/deluno", headers=SECRET, json=body, timeout=30)


def wait_for_poster(admin: WeirClient) -> str:
    def poster() -> str | None:
        listed = admin.get(f"{API}/processing/files")
        assert listed.status_code == 200, listed.text
        return next((entry["poster_url"] for entry in listed.json()["files"] if entry["poster_url"]), None)

    return wait_until(poster, what="the hand-off's file to show a poster")


def test_a_hand_off_with_none_of_the_poster_fields_is_looked_up_by_its_release_name(
    started: tuple[ServerUnderTest, WeirClient, LibraryFolders], fake_gateway: FakeGateway
) -> None:
    server, admin, folders = started
    fake_gateway.knows("metropolis", "metropolis.jpg")

    assert hand_off(server, folders).status_code == 200

    wait_for_poster(admin)
    assert fake_gateway.searches()[0].query == {"mediaType": ["movies"], "query": ["metropolis"], "year": ["1927"]}


def test_a_hand_off_with_a_tmdb_id_is_looked_up_exactly(
    started: tuple[ServerUnderTest, WeirClient, LibraryFolders], fake_gateway: FakeGateway
) -> None:
    server, admin, folders = started
    fake_gateway.knows("a different title", "metropolis.jpg", tmdb_id=19)

    response = hand_off(server, folders, title="Metropolis", year=1927, tmdbId=19, imdbId="tt0017136")

    assert response.status_code == 200, response.text
    wait_for_poster(admin)
    search = fake_gateway.searches()[0]
    assert search.query["providerId"] == ["19"]
    assert search.query["query"] == ["Metropolis"]


def test_a_poster_address_in_a_hand_off_is_used_without_a_search(
    started: tuple[ServerUnderTest, WeirClient, LibraryFolders], fake_gateway: FakeGateway
) -> None:
    server, admin, folders = started
    fake_gateway.serves_image("fromdeluno.jpg")

    response = hand_off(server, folders, tmdbId=19, posterUrl="https://image.tmdb.org/t/p/w500/fromdeluno.jpg")

    assert response.status_code == 200, response.text
    wait_for_poster(admin)
    assert fake_gateway.searches() == []
    assert [r.path for r in fake_gateway.image_requests()] == ["/artwork/w342/fromdeluno.jpg"]


def test_a_poster_address_on_another_host_is_ignored_and_the_title_decides(
    started: tuple[ServerUnderTest, WeirClient, LibraryFolders], fake_gateway: FakeGateway
) -> None:
    server, admin, folders = started
    fake_gateway.knows("metropolis", "metropolis.jpg")

    response = hand_off(server, folders, posterUrl="https://evil.example/t/p/w500/fromdeluno.jpg")

    assert response.status_code == 200, response.text
    wait_for_poster(admin)
    assert len(fake_gateway.searches_for("metropolis")) == 1


def test_a_hand_off_with_malformed_poster_fields_is_still_accepted(
    started: tuple[ServerUnderTest, WeirClient, LibraryFolders], fake_gateway: FakeGateway
) -> None:
    server, admin, folders = started
    fake_gateway.knows("metropolis", "metropolis.jpg")

    response = hand_off(server, folders, year="soon", tmdbId="abc", imdbId=12, season=-3, posterUrl=7)

    assert response.status_code == 200, response.text
    wait_for_poster(admin)
    never_within(
        lambda: len(fake_gateway.searches_for("metropolis")) > 1,
        seconds=1.0,
        what="a second search for the same title",
    )
