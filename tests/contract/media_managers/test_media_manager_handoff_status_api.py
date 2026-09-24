"""Hand-off status for media managers: auth and the states a poll can read.

Running work, cancelling and retention are in ``test_media_manager_handoff_lifecycle_api.py``;
folder hand-offs are in ``test_media_manager_handoff_folder_api.py``. All three share fixtures
from ``conftest.py`` and helpers from ``_handoff_status_helpers.py``.
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta
from pathlib import Path

import httpx
import pytest

from tests.contract.media_managers._handoff_status_helpers import (
    SECRET,
    SECRET_ENV,
    _hand_off,
    _new_id,
    _seed,
    _status,
    _status_response,
)
from tests.contract.media_managers._helpers import NO_WEBHOOK_SECRET, LibraryFolders, ensure_library, handoff_dedupe_key
from tests.contract.support import seed
from tests.contract.support.client import API
from tests.contract.support.launcher import ServerUnderTest

# --- auth --------------------------------------------------------------------------------------


def test_no_configured_secret_is_a_403_that_says_what_to_set(server_factory) -> None:
    sut = server_factory(dict(NO_WEBHOOK_SECRET))
    for method, path in (
        ("GET", f"{API}/intake/capabilities"),
        ("GET", f"{API}/intake/handoffs/deluno/h1"),
        ("DELETE", f"{API}/intake/handoffs/deluno/h1"),
    ):
        response = httpx.request(method, f"{sut.base_url}{path}", timeout=30)
        assert response.status_code == 403, (method, path)
        assert "Set a webhook secret" in response.json()["detail"]


def test_a_wrong_or_missing_secret_is_refused(server: ServerUnderTest) -> None:
    assert _status_response(server, "h1", headers={}).status_code == 401
    assert _status_response(server, "h1", headers={"X-Webhook-Secret": "no"}).status_code == 401
    wrong = httpx.get(f"{server.base_url}{API}/intake/capabilities", headers={"X-Webhook-Secret": "no"}, timeout=30)
    assert wrong.status_code == 401


def test_capabilities_name_both_abilities(server: ServerUnderTest) -> None:
    response = httpx.get(f"{server.base_url}{API}/intake/capabilities", headers=SECRET, timeout=30)
    assert response.status_code == 200
    assert response.json() == {
        "capabilities": ["handoff-status", "handoff-cancel", "handoff-outcome", "handoff-outcome-codes"]
    }


# --- status ------------------------------------------------------------------------------------


def test_an_unknown_hand_off_is_404(server: ServerUnderTest) -> None:
    assert _status_response(server, "nope").status_code == 404


def test_a_new_hand_off_is_queued_with_its_place(server_factory, client_factory, tmp_path: Path) -> None:
    # A server of its own, so no other test's queued work is ahead of this one.
    sut = server_factory(dict(SECRET_ENV))
    admin = client_factory(sut)
    admin.ensure_admin()
    folders = LibraryFolders.make(tmp_path / "movies")
    ensure_library(admin, name="Movies", media_type="movie", folders=folders)

    _hand_off(sut, "h1", folders.watched / "Film" / "film.mkv")
    body = _status(sut, "h1")
    assert body["handoffId"] == "h1"
    assert body["state"] == "queued"
    assert body["queuePosition"] == 1
    assert body["lastChangedUtc"].endswith("Z")


def test_a_pending_retry_is_scheduled_for_when_it_will_run(server: ServerUnderTest, movies: LibraryFolders) -> None:
    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    retry_at = datetime.now(UTC) + timedelta(minutes=20)
    _seed(
        server,
        hid,
        job_status="completed",
        file_status="processing_failed",
        next_retry_at=seed.utc_text(retry_at),
        status_reason="ffmpeg died.",
    )
    body = _status(server, hid)
    assert body["state"] == "scheduled"
    assert body["scheduledFor"].startswith(retry_at.strftime("%Y-%m-%dT%H:%M"))


def test_a_retry_still_owed_after_its_backoff_is_scheduled_not_failed(
    server: ServerUnderTest, movies: LibraryFolders
) -> None:
    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    retry_at = datetime.now(UTC) - timedelta(minutes=5)
    _seed(
        server,
        hid,
        job_status="completed",
        file_status="processing_failed",
        next_retry_at=seed.utc_text(retry_at),
        failure_attempts=1,
        status_reason="ffmpeg died.",
    )
    body = _status(server, hid)
    assert body["state"] == "scheduled"
    assert body["scheduledFor"].startswith(retry_at.strftime("%Y-%m-%dT%H:%M"))


def test_an_overdue_retry_still_reads_scheduled_not_failed(server: ServerUnderTest, movies: LibraryFolders) -> None:
    """#531 item 3: once the backoff has elapsed but no scan has picked the file up yet, the hand-off
    still reads ``scheduled``.

    A retry is still coming (a scan can be up to five minutes away by default), and Deluno treats
    ``failed`` as final, so reporting ``failed`` would make it give up on a hand-off about to succeed.
    """

    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    overdue = datetime.now(UTC) - timedelta(seconds=5)
    _seed(
        server,
        hid,
        job_status="completed",
        file_status="processing_failed",
        next_retry_at=seed.utc_text(overdue),
        status_reason="ffmpeg died.",
    )
    body = _status(server, hid)
    assert body["state"] == "scheduled", "a retry that is merely overdue for its next scan is not a final failure"


def test_waiting_for_the_manager_is_queued_not_stalled(server: ServerUnderTest, movies: LibraryFolders) -> None:
    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    _seed(
        server,
        hid,
        job_status="completed",
        file_status="blocked_upstream",
        status_reason="Deluno is still importing this file.",
    )
    body = _status(server, hid)
    assert body["state"] == "queued"
    assert body["message"] == "Deluno is still importing this file."


@pytest.mark.parametrize(
    ("file_status", "state"),
    [
        ("processed", "completed"),
        ("passed_through", "passed-through"),
        ("rejected", "rejected"),
        ("processing_failed", "failed"),
        ("out_of_schedule", "scheduled"),
    ],
)
def test_the_file_state_maps_to_the_agreed_vocabulary(
    server: ServerUnderTest, movies: LibraryFolders, file_status: str, state: str
) -> None:
    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    _seed(server, hid, job_status="completed", file_status=file_status)
    assert _status(server, hid)["state"] == state


def test_a_finished_hand_off_still_answers_after_job_rows_are_pruned(
    server: ServerUnderTest, movies: LibraryFolders
) -> None:
    """The reason the ledger exists. "Never heard of it" here would import the unprocessed original."""

    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    _seed(server, hid, job_status="completed", file_status="processed")
    assert _status(server, hid)["state"] == "completed"

    # What job-row retention and "clear history" leave behind: no job row, no Files row.
    with seed.stopped(server) as conn:
        key = handoff_dedupe_key(hid)
        assert conn.execute("DELETE FROM jobs WHERE dedupe_key = ?", (key,)).rowcount == 1
        assert conn.execute("DELETE FROM files WHERE relative_path = ?", (f"{hid}/film.mkv",)).rowcount == 1

    assert _status(server, hid)["state"] == "completed"


def test_a_repeated_poll_does_not_move_last_changed(server: ServerUnderTest, movies: LibraryFolders) -> None:
    """Deluno reads an unchanged timestamp as "nothing happened". Polling must not reset it."""

    hid = _new_id()
    _hand_off(server, hid, movies.watched / hid / "film.mkv")
    first = _status(server, hid)["lastChangedUtc"]
    assert _status(server, hid)["lastChangedUtc"] == first
