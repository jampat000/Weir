"""Hand-off lifecycle for media managers: running work, cancelling, and retention.

Auth and status are in ``test_media_manager_handoff_status_api.py``; folder hand-offs are in
``test_media_manager_handoff_folder_api.py``. All three share fixtures from ``conftest.py`` and
helpers from ``_handoff_status_helpers.py``.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Any

from tests.contract.media_managers._handoff_status_helpers import (
    LEDGER_RETENTION_DAYS,
    SECRET_ENV,
    _cancel,
    _hand_off,
    _new_id,
    _status,
    _status_response,
)
from tests.contract.media_managers._helpers import (
    CALLBACK_PATH,
    LibraryFolders,
    create_connection,
    create_library,
    payload,
    remux_jobs,
    update_library,
)
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.fake_ffmpeg import FakeFfmpeg, fake_media_bytes, probe
from tests.contract.support.launcher import ServerUnderTest
from tests.contract.support.polling import wait_until

# --- running work: a real worker with a fake ffmpeg ------------------------------------------------


@dataclass
class WorkingServer:
    server: ServerUnderTest
    admin: WeirClient
    folders: LibraryFolders


def _working_server(
    server_factory, client_factory, fake_managers, fake_ffmpeg: FakeFfmpeg, root: Path
) -> tuple[WorkingServer, Any]:
    """One worker, fake tools, a Movies library that takes a fresh file at once, and a fake Deluno."""

    sut = server_factory({**SECRET_ENV, **fake_ffmpeg.env, "WEIR_PROCESSING_WORKER_COUNT": "1"})
    admin = client_factory(sut)
    admin.ensure_admin()
    relaxed = admin.put_csrf(
        f"{API}/processing/operator-settings",
        {"min_file_age_seconds": 0, "min_input_file_size_mb": 0, "minimum_free_disk_space_mb": 0},
    )
    assert relaxed.status_code == 200, relaxed.text
    folders = LibraryFolders.make(root / "library")
    work = root / "library" / "work"
    work.mkdir(parents=True, exist_ok=True)
    listed = admin.get(f"{API}/processing/libraries").json()
    for row in listed:
        if row["media_type"] == "movie":
            update_library(
                admin,
                row,
                watched_folder=str(folders.watched),
                output_folder=str(folders.output),
                work_folder=str(work),
                min_file_age_seconds=0,
                file_detection_interval_seconds=0,
                skip_access_tests=True,
                min_file_size_mb=0,
            )
            break
    else:
        create_library(
            admin,
            name="Movies",
            media_type="movie",
            folders=folders,
            work_folder=str(work),
            min_file_age_seconds=0,
            file_detection_interval_seconds=0,
            skip_access_tests=True,
            min_file_size_mb=0,
        )
    deluno = fake_managers("deluno")
    created = create_connection(admin, name="Deluno", base_url=deluno.base_url, api_key=deluno.api_key)
    assert created.status_code == 201, created.text
    source = folders.watched / "Film" / "film.mkv"
    source.parent.mkdir(parents=True, exist_ok=True)
    source.write_bytes(fake_media_bytes(probe()))
    return WorkingServer(sut, admin, folders), deluno


def _job_status(admin: WeirClient, handoff_id: str) -> str | None:
    """The newest remux job for this hand-off. Matched on the payload: cancelling renames the dedupe key."""

    for job in remux_jobs(admin):
        if (payload(job).get("origin") or {}).get("handoff_id") == handoff_id:
            return job["status"]
    return None


def test_a_running_pass_is_working(server_factory, client_factory, fake_managers, fake_ffmpeg, tmp_path) -> None:
    fake_ffmpeg.set_file_rule("*.mkv", probe=probe(), remux_delay_seconds=20)
    working, _ = _working_server(server_factory, client_factory, fake_managers, fake_ffmpeg, tmp_path)
    _hand_off(working.server, "h1", working.folders.watched / "Film" / "film.mkv")

    wait_until(lambda: _job_status(working.admin, "h1") == "leased", timeout_s=60, what="the worker to take the job")
    assert _status(working.server, "h1")["state"] == "working"


def test_the_report_records_the_output_path(
    server_factory, client_factory, fake_managers, fake_ffmpeg, tmp_path
) -> None:
    # A worker finishes the file and reports.
    fake_ffmpeg.set_file_rule("*.mkv", probe=probe())
    working, deluno = _working_server(server_factory, client_factory, fake_managers, fake_ffmpeg, tmp_path)
    _hand_off(working.server, "h1", working.folders.watched / "Film" / "film.mkv")

    body = wait_until(
        lambda: (answer := _status(working.server, "h1"))["state"] not in ("queued", "working") and answer,
        timeout_s=90,
        what="the hand-off to finish",
    )
    deluno.wait_for_request("POST", CALLBACK_PATH)
    assert body["state"] == "completed", body
    assert body["outputPath"]
    output = Path(body["outputPath"])
    assert output.name == "film.mkv"
    assert output.is_file()
    assert working.folders.output.resolve() in output.resolve().parents


# --- cancel ------------------------------------------------------------------------------------


def test_a_queued_hand_off_can_be_cancelled(server: ServerUnderTest, movies: LibraryFolders, client_factory) -> None:
    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    response = _cancel(server, hid)
    assert response.status_code == 204, response.text

    admin = client_factory(server)
    admin.ensure_admin()
    assert _job_status(admin, hid) == "cancelled"
    events = admin.get(f"{API}/activity/recent", params={"event_type": "processing.handoff_cancelled", "limit": 100})
    assert events.status_code == 200, events.text
    titles = [item["title"] for item in events.json()["items"]]
    assert any("film.mkv" in title for title in titles), titles
    assert _status(server, hid)["state"] == "cancelled"
    # A second cancel is refused: it is already finished.
    assert _cancel(server, hid).status_code == 409


def test_work_that_has_started_is_never_cancelled(
    server_factory, client_factory, fake_managers, fake_ffmpeg, tmp_path
) -> None:
    fake_ffmpeg.set_file_rule("*.mkv", probe=probe(), remux_delay_seconds=20)
    working, _ = _working_server(server_factory, client_factory, fake_managers, fake_ffmpeg, tmp_path)
    _hand_off(working.server, "h1", working.folders.watched / "Film" / "film.mkv")
    wait_until(lambda: _job_status(working.admin, "h1") == "leased", timeout_s=60, what="the worker to take the job")

    response = _cancel(working.server, "h1")
    assert response.status_code == 409
    assert "working" in response.json()["detail"]
    assert _job_status(working.admin, "h1") == "leased"


def test_cancelling_an_unknown_hand_off_is_404(server: ServerUnderTest) -> None:
    assert _cancel(server, "nope").status_code == 404


def test_a_manager_resending_a_cancelled_hand_off_starts_it_again(
    server: ServerUnderTest, movies: LibraryFolders
) -> None:
    hid = _new_id()
    source = movies.watched / hid / "film.mkv"
    _hand_off(server, hid, source)
    assert _cancel(server, hid).status_code == 204
    _hand_off(server, hid, source)
    assert _status(server, hid)["state"] == "queued"


# --- retention ---------------------------------------------------------------------------------


def test_only_old_finished_hand_offs_are_pruned(server: ServerUnderTest) -> None:
    # The retention tick that runs at startup does the pruning.
    old = seed.utc_text(datetime.now(UTC) - timedelta(days=LEDGER_RETENTION_DAYS + 1))
    done, waiting = _new_id(), _new_id()
    with seed.stopped(server) as conn:
        for handoff_id, state in ((done, "completed"), (waiting, "queued")):
            conn.execute(
                "INSERT INTO media_manager_handoffs (source_key, handoff_id, relative_path, state, created_at, "
                "last_changed_at) VALUES (?, ?, ?, ?, ?, ?)",
                ("deluno", handoff_id, "x.mkv", state, old, old),
            )

    wait_until(
        lambda: _status_response(server, done).status_code == 404,
        timeout_s=30,
        what="the old finished hand-off to be pruned",
    )
    assert _status(server, waiting)["state"] == "queued"
