"""Folder hand-offs for media managers: Deluno names the completed download's folder, not a file.

Auth and status are in ``test_media_manager_handoff_status_api.py``; running work, cancelling and
retention are in ``test_media_manager_handoff_lifecycle_api.py``. All three share fixtures from
``conftest.py`` and helpers from ``_handoff_status_helpers.py``.
"""

from __future__ import annotations

import uuid
from pathlib import Path
from typing import Any

from tests.contract.media_managers._handoff_status_helpers import _hand_off, _hand_off_response, _new_id, _seed, _status
from tests.contract.media_managers._helpers import (
    LibraryFolders,
    create_library,
    handoff_dedupe_key,
    payload,
    remux_jobs,
)
from tests.contract.support.client import WeirClient
from tests.contract.support.launcher import ServerUnderTest


def _folder_library(admin: WeirClient, root: Path) -> LibraryFolders:
    folders = LibraryFolders.make(root)
    create_library(admin, name=f"Folder {uuid.uuid4().hex[:8]}", media_type="movie", folders=folders)
    return folders


def test_a_folder_hand_off_queues_the_video_inside_it_not_the_folder_or_the_sample(
    server: ServerUnderTest, admin: WeirClient, tmp_path: Path
) -> None:
    folders = _folder_library(admin, tmp_path / "folder-library")
    watched = folders.watched
    (watched / "Blade.Runner.2049" / "Sample").mkdir(parents=True)
    (watched / "Blade.Runner.2049" / "Blade.Runner.2049.mkv").write_bytes(b"x")
    (watched / "Blade.Runner.2049" / "Sample" / "sample.mkv").write_bytes(b"x")
    (watched / "Blade.Runner.2049" / "movie.nfo").write_text("x")
    hid = _new_id()
    key = handoff_dedupe_key(hid)

    def jobs() -> list[dict[str, Any]]:
        return [j for j in remux_jobs(admin) if j["dedupe_key"] == key or j["dedupe_key"].startswith(f"{key}:")]

    assert _hand_off_response(server, hid, watched / "Blade.Runner.2049").status_code == 200
    (job,) = jobs()
    assert '"relative_media_path":"Blade.Runner.2049/Blade.Runner.2049.mkv"' in (job["payload_json"] or "")
    assert payload(job)["relative_media_path"] == "Blade.Runner.2049/Blade.Runner.2049.mkv"
    # A repeated hand-off returns the same job rather than queueing the file twice.
    assert _hand_off_response(server, hid, watched / "Blade.Runner.2049").status_code == 200
    assert len(jobs()) == 1


def test_a_folder_with_no_video_is_refused_with_a_reason(
    server: ServerUnderTest, admin: WeirClient, tmp_path: Path
) -> None:
    folders = _folder_library(admin, tmp_path / "folder-library")
    (folders.watched / "Empty.Release").mkdir(parents=True)
    (folders.watched / "Empty.Release" / "readme.txt").write_text("x")
    response = _hand_off_response(server, _new_id(), folders.watched / "Empty.Release")
    assert response.status_code == 400
    assert "no video file" in response.json()["detail"]


def test_a_folder_hand_off_answers_for_the_files_inside_it(
    server: ServerUnderTest, admin: WeirClient, tmp_path: Path
) -> None:
    folders = _folder_library(admin, tmp_path / "folder-library")
    (folders.watched / "Film").mkdir(parents=True)
    (folders.watched / "Film" / "film.mkv").write_bytes(b"x")
    hid = _new_id()
    assert _hand_off_response(server, hid, folders.watched / "Film").status_code == 200
    _seed(server, hid, job_status="completed", file_status="processed", relative_path="Film/film.mkv")
    assert _status(server, hid)["state"] == "completed"


def test_a_file_held_after_repeated_failures_is_failed_not_queued(
    server: ServerUnderTest, movies: LibraryFolders
) -> None:
    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    _seed(server, hid, job_status="completed", file_status="on_hold", failure_attempts=3)
    assert _status(server, hid)["state"] == "failed"
