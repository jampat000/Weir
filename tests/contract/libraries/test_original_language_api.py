"""The original language of a title comes from Deluno's metadata service, with nothing to set up. The metadata service is
a fake; no test reaches the real one. A rules preview runs the same lookup a live pass runs, so it shows what the rules
would keep."""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from tests.contract.libraries import _helpers as h
from tests.contract.processing import _helpers as processing
from tests.contract.support.client import API, WeirClient
from tests.contract.support.fake_ffmpeg import FakeFfmpeg, fake_media_bytes, probe
from tests.contract.support.fake_gateway import FakeGateway
from tests.contract.support.launcher import ServerUnderTest

FILM = "Nosferatu (1922)/Nosferatu.1922.1080p.mkv"


@dataclass
class Film:
    """A library with one film in it, and a profile that keeps the title's original language."""

    admin: WeirClient
    library_id: int
    profile_id: int

    def preview(self) -> dict[str, Any]:
        previewed = self.admin.post_csrf(
            f"{API}/processing/libraries/{self.library_id}/preview",
            {"relative_path": FILM, "rule_set_id": self.profile_id},
        )
        assert previewed.status_code == 200, previewed.text
        return previewed.json()["original_language"]


def start_with_a_film(
    server_factory: Callable[..., ServerUnderTest],
    client_factory: Callable[..., WeirClient],
    fake_ffmpeg: FakeFfmpeg,
    fake_gateway: FakeGateway,
    tmp_path: Path,
) -> Film:
    env = {**fake_ffmpeg.env, "WEIR_PROCESSING_WORKER_COUNT": "0", "WEIR_ARTWORK_GATEWAY_URL": fake_gateway.base_url}
    server = server_factory(env=env)
    admin = h.signed_in_admin(server, client_factory)
    folders = processing.Folders.make(tmp_path)
    library = processing.create_library(admin, folders)
    source = folders.watched / FILM
    source.parent.mkdir(parents=True)
    source.write_bytes(fake_media_bytes(probe(audio_languages=("eng", "ger"))))
    profile = admin.post_csrf(
        f"{API}/processing/rule-sets",
        {"name": "Keep the original", "primary_audio_lang": "eng", "keep_original_language": True},
    )
    assert profile.status_code == 201, profile.text
    return Film(admin, library["id"], profile.json()["id"])


def test_a_rules_preview_takes_the_original_language_from_the_metadata_service(
    server_factory: Callable[..., ServerUnderTest],
    client_factory: Callable[..., WeirClient],
    fake_ffmpeg: FakeFfmpeg,
    fake_gateway: FakeGateway,
    tmp_path: Path,
) -> None:
    fake_gateway.knows("nosferatu", "nosferatu.jpg", tmdb_id=653, original_language="de")
    film = start_with_a_film(server_factory, client_factory, fake_ffmpeg, fake_gateway, tmp_path)

    original = film.preview()

    assert original["lookup_status"] == "matched"
    assert original["original_language"] == "de"
    search = fake_gateway.searches()[0]
    assert search.query == {"mediaType": ["movies"], "query": ["nosferatu"], "year": ["1922"]}


def test_a_title_is_asked_about_once_however_often_its_language_is_needed(
    server_factory: Callable[..., ServerUnderTest],
    client_factory: Callable[..., WeirClient],
    fake_ffmpeg: FakeFfmpeg,
    fake_gateway: FakeGateway,
    tmp_path: Path,
) -> None:
    fake_gateway.knows("nosferatu", "nosferatu.jpg", original_language="de")
    film = start_with_a_film(server_factory, client_factory, fake_ffmpeg, fake_gateway, tmp_path)

    film.preview()
    again = film.preview()

    assert again["original_language"] == "de"
    assert len(fake_gateway.searches()) == 1


def test_a_title_the_service_does_not_know_leaves_the_language_preferences_in_charge(
    server_factory: Callable[..., ServerUnderTest],
    client_factory: Callable[..., WeirClient],
    fake_ffmpeg: FakeFfmpeg,
    fake_gateway: FakeGateway,
    tmp_path: Path,
) -> None:
    film = start_with_a_film(server_factory, client_factory, fake_ffmpeg, fake_gateway, tmp_path)

    original = film.preview()

    assert original["lookup_status"] == "no_match"
    assert "language preferences" in original["note"]
