"""Basic contract coverage for Processing files, requeue, the file log and why-held: auth, viewer versus
operator, status codes, response keys, filters.
"""

from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta

import pytest

from tests.contract.libraries import _helpers as h
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient

FILES = f"{API}/processing/files"

FILE_KEYS = {
    "id",
    "library_id",
    "library_name",
    "relative_path",
    "status",
    "status_reason",
    "blocked_by_connection",
    "size_bytes",
    "video_codec",
    "video_width",
    "video_height",
    "audio_track_count",
    "subtitle_track_count",
    "duration_seconds",
    "direct_play",
    "progress_percent",
    "progress_message",
    "progress_eta_seconds",
    "failure_class",
    "failure_attempts",
    "quarantined",
    "next_retry_at",
    "output_collision_policy",
    "output_collision_action",
    "output_collision_reason",
    "hold_until",
    "size_changed_at",
    "created_at",
    "updated_at",
    "last_seen_at",
    "last_attempt_at",
}


@pytest.fixture(scope="module", autouse=True)
def seeded(server) -> None:
    """A viewer, and file rows across both seeded libraries (seeded while the server is stopped)."""

    with seed.stopped(server) as conn:
        seed.insert_user(conn, username=h.VIEWER_USERNAME, password=h.VIEWER_PASSWORD, role="viewer")
        movies = int(seed.scalar(conn, "SELECT id FROM libraries WHERE name = 'Movies'"))
        tv = int(seed.scalar(conn, "SELECT id FROM libraries WHERE name = 'TV'"))
        alpha = h.insert_file(
            conn,
            library_id=movies,
            relative_path="Alpha/alpha.mkv",
            status="on_hold",
            status_reason="Held for the test.",
            failure_class="execution",
            failure_attempts=3,
            size_bytes=4096,
            last_seen_at=seed.utc_text(),
        )
        h.insert_file(
            conn,
            library_id=movies,
            relative_path="Beta/beta.mkv",
            status="processed",
            last_seen_at=seed.utc_text(datetime.now(UTC) - timedelta(days=400)),
        )
        h.insert_file(conn, library_id=tv, relative_path="Show/S01E01.mkv", status="skipped")
        h.insert_file(
            conn,
            library_id=tv,
            relative_path="Requeue/one.mkv",
            status="processing_failed",
            failure_class="execution",
            failure_attempts=2,
        )
        h.insert_file(conn, library_id=movies, relative_path="BulkRequeue/x1.mkv", status="processing_failed")
        h.insert_file(conn, library_id=movies, relative_path="BulkRequeue/x2.mkv", status="processing_failed")
        conn.execute(
            "INSERT INTO file_logs (file_id, library_id, relative_path, library_name, outcome, title, "
            "detail_json, recorded_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            (
                alpha,
                movies,
                "Alpha/alpha.mkv",
                "Movies",
                "failed",
                "Remux failed",
                json.dumps({"outcome": "failed", "reason": "boom"}),
                seed.utc_text(),
            ),
        )


def _file(c: WeirClient, relative_path: str) -> dict:
    r = c.get(FILES, params={"path_contains": relative_path})
    assert r.status_code == 200, r.text
    matches = [f for f in r.json()["files"] if f["relative_path"] == relative_path]
    assert len(matches) == 1, r.text
    return matches[0]


# --- files ------------------------------------------------------------------------------------------


def test_files_require_a_session(client) -> None:
    assert client.get(FILES).status_code == 401


def test_files_list_shape(viewer) -> None:
    r = viewer.get(FILES)
    assert r.status_code == 200, r.text
    body = r.json()
    assert set(body) >= {"files", "status_counts", "returned", "limit"}
    assert body["limit"] == 200
    assert body["returned"] == len(body["files"]) >= 6
    assert isinstance(body["status_counts"], dict)
    assert set(body["files"][0]) >= FILE_KEYS

    alpha = _file(viewer, "Alpha/alpha.mkv")
    assert alpha["library_name"] == "Movies"
    assert alpha["status"] == "on_hold"
    assert alpha["status_reason"] == "Held for the test."
    assert alpha["size_bytes"] == 4096
    assert alpha["failure_attempts"] == 3
    assert alpha["quarantined"] is True
    assert alpha["direct_play"] == []


def test_files_filters(admin) -> None:
    libraries = {row["name"]: row["id"] for row in admin.get(f"{API}/processing/libraries").json()}

    by_library = admin.get(FILES, params={"library_id": libraries["TV"]}).json()
    assert by_library["files"]
    assert {f["library_id"] for f in by_library["files"]} == {libraries["TV"]}

    processed = admin.get(FILES, params={"file_status": "processed"}).json()
    assert [f["relative_path"] for f in processed["files"]] == ["Beta/beta.mkv"]

    by_path = admin.get(FILES, params={"path_contains": "Alpha"}).json()
    assert [f["relative_path"] for f in by_path["files"]] == ["Alpha/alpha.mkv"]

    # within_days filters on when a scan last saw the file.
    recent = {f["relative_path"] for f in admin.get(FILES, params={"within_days": 30}).json()["files"]}
    assert "Alpha/alpha.mkv" in recent
    assert "Beta/beta.mkv" not in recent
    assert "Show/S01E01.mkv" not in recent

    limited = admin.get(FILES, params={"limit": 1}).json()
    assert limited["returned"] == 1
    assert limited["limit"] == 1
    assert len(limited["files"]) == 1


@pytest.mark.parametrize(
    "params",
    [{"file_status": "not_a_status"}, {"limit": 0}, {"limit": 1001}, {"library_id": 0}, {"within_days": 0}],
)
def test_files_reject_invalid_filters(admin, params: dict) -> None:
    assert admin.get(FILES, params=params).status_code == 422


def test_requeue_one_file(admin, viewer, client_factory, server) -> None:
    target = _file(admin, "Requeue/one.mkv")
    path = f"{FILES}/{target['id']}/requeue"

    anonymous = client_factory(server)
    assert anonymous.post_csrf(path).status_code == 401
    assert viewer.post_csrf(path).status_code == 403
    assert admin.post_csrf(f"{FILES}/999999/requeue").status_code == 404

    r = admin.post_csrf(path)
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["requeued"] == 1
    assert body["skipped"] == 0
    assert body["detail"]

    after = _file(admin, "Requeue/one.mkv")
    assert after["status"] == "unprocessed"
    assert after["failure_attempts"] == 0
    assert after["failure_class"] is None
    assert after["status_reason"] == body["detail"]


def test_requeue_without_a_csrf_token_is_refused(admin) -> None:
    target = _file(admin, "Requeue/one.mkv")
    assert admin.post(f"{FILES}/{target['id']}/requeue", json={}).status_code == 422
    assert admin.post(f"{FILES}/{target['id']}/requeue", json={"csrf_token": "forged"}).status_code == 400


def test_bulk_requeue(admin, viewer) -> None:
    bulk = f"{FILES}/requeue"
    assert viewer.post_csrf(bulk, {"path_contains": "BulkRequeue"}).status_code == 403

    nothing = admin.post_csrf(bulk, {"path_contains": "no-file-has-this-in-its-path"})
    assert nothing.status_code == 200, nothing.text
    assert nothing.json()["requeued"] == 0
    assert nothing.json()["skipped"] == 0

    r = admin.post_csrf(bulk, {"path_contains": "BulkRequeue", "file_status": "processing_failed"})
    assert r.status_code == 200, r.text
    assert r.json()["requeued"] == 2
    assert r.json()["skipped"] == 0
    for relative_path in ("BulkRequeue/x1.mkv", "BulkRequeue/x2.mkv"):
        assert _file(admin, relative_path)["status"] == "unprocessed"


def test_file_log(viewer) -> None:
    assert viewer.get(f"{FILES}/999999/log").status_code == 404

    alpha = _file(viewer, "Alpha/alpha.mkv")
    r = viewer.get(f"{FILES}/{alpha['id']}/log")
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["file_id"] == alpha["id"]
    assert body["relative_path"] == "Alpha/alpha.mkv"
    assert isinstance(body["retention_days"], int)
    assert len(body["entries"]) == 1
    entry = body["entries"][0]
    assert set(entry) >= {"id", "recorded_at", "outcome", "title", "library_name", "detail", "story"}
    assert entry["outcome"] == "failed"
    assert entry["title"] == "Remux failed"
    assert entry["library_name"] == "Movies"
    assert entry["detail"]["reason"] == "boom"
    assert isinstance(entry["story"], list)

    beta = _file(viewer, "Beta/beta.mkv")
    empty = viewer.get(f"{FILES}/{beta['id']}/log").json()
    assert empty["entries"] == []


def test_file_log_needs_a_session(client) -> None:
    assert client.get(f"{FILES}/1/log").status_code == 401


def test_why_held(viewer, client) -> None:
    assert client.get(f"{FILES}/1/why-held").status_code == 401
    assert viewer.get(f"{FILES}/999999/why-held").status_code == 404

    alpha = _file(viewer, "Alpha/alpha.mkv")
    r = viewer.get(f"{FILES}/{alpha['id']}/why-held")
    assert r.status_code == 200, r.text
    body = r.json()
    assert set(body) >= {
        "file_id",
        "relative_path",
        "library_name",
        "recorded_status",
        "recorded_reason",
        "verdict",
        "owned",
        "blocked_upstream",
        "blocked_by_connection",
        "queue_row_count",
        "managers_consulted",
        "managers_reporting",
        "managers_without_queue_signal",
        "reasons",
    }
    assert body["file_id"] == alpha["id"]
    assert body["relative_path"] == "Alpha/alpha.mkv"
    assert body["library_name"] == "Movies"
    assert body["recorded_status"] == "on_hold"
    assert body["recorded_reason"] == "Held for the test."
    assert body["verdict"] in {"proceed", "wait_upstream", "not_held", "no_upstream_signal"}
    # No media manager is connected on this install, so nobody was asked.
    assert body["managers_consulted"] == 0
    assert isinstance(body["reasons"], list)
