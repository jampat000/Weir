"""System › Logs: Weir's events, jobs and server log as one list, filtered, counted, paged and exported."""

from __future__ import annotations

import csv
import io
import json
from datetime import UTC, datetime, timedelta
from typing import Any

import pytest

from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.system import _helpers as h
from tests.contract.system import _log_seed as seeded

LOG = f"{API}/system/log"
EXPORT = f"{API}/system/log/export"


@pytest.fixture(scope="module", autouse=True)
def _seeded(server) -> dict[str, int]:
    h.seed_users(server)
    logs = server.home / "logs"
    logs.mkdir(parents=True, exist_ok=True)
    with seed.stopped(server) as conn:
        conn.execute("DELETE FROM jobs")
        library_id = int(conn.execute("SELECT id FROM libraries ORDER BY id LIMIT 1").fetchone()[0])
        seeded.event(conn, seeded.at(10), "auth.login_succeeded", "Signed in", result="success", trigger="manual")
        seeded.event(
            conn,
            seeded.at(11),
            "library.scan_completed",
            "Movies scanned",
            result="success",
            trigger="scheduled",
            library_id=library_id,
        )
        seeded.event(conn, seeded.at(12), "auth.login_failed", "Sign-in failed", result="failed", trigger="manual")
        seeded.event(
            conn,
            seeded.at(13),
            "processing.file_remux_pass_completed",
            "Heat processed",
            result="success",
            library_id=library_id,
            relative_path="Heat/heat.mkv",
        )
        failed = seeded.job(
            conn,
            seeded.at(20),
            "contract:failed",
            "processing.file.remux_pass.v1",
            "failed",
            last_error="ffmpeg stopped",
            library_id=library_id,
        )
        seeded.job(
            conn, seeded.at(21), "contract:queued", "processing.file.remux_pass.v1", "pending", library_id=library_id
        )
        seeded.job(conn, seeded.at(22), "contract:cleanup", "processing.work_temp_stale_sweep.v1", "completed")
        seeded.job(
            conn,
            seeded.at(23),
            "contract:routine-scan",
            "processing.watched_folder.remux_scan_dispatch.v1",
            "completed",
        )
        # Written while the server is stopped, since it holds its log open while it runs.
        with (logs / "weir.log").open("a", encoding="utf-8") as log:
            warning = seeded.log_line(
                seeded.at(30), "WARNING", "weir.platform.suite_settings.backups", "The backup folder is nearly full"
            )
            error = seeded.log_line(
                seeded.at(31),
                "ERROR",
                "weir.processing",
                "The pass stopped",
                job_id=str(failed),
                traceback="System.InvalidOperationException: ffmpeg stopped",
            )
            log.write(f"{warning}\n{error}\n")
    return {"library_id": library_id, "failed_job": failed}


@pytest.fixture
def viewer(server, client_factory) -> WeirClient:
    return h.signed_in_viewer(server, client_factory)


def _titles(body: dict[str, Any]) -> list[str]:
    return [item["title"] for item in body["items"]]


def _get(client: WeirClient, **params: Any) -> dict[str, Any]:
    r = client.get(LOG, params={**seeded.WINDOW, **params})
    assert r.status_code == 200, r.text
    return r.json()


def test_the_log_needs_a_session(client) -> None:
    assert client.get(LOG).status_code == 401
    assert client.get(EXPORT).status_code == 401


def test_the_log_is_one_newest_first_list_of_every_source(admin) -> None:
    body = _get(admin)

    assert set(body) == {"items", "next_cursor", "total", "counts"}
    assert [item["source"] for item in body["items"]] == [
        "server",
        "server",
        "job",
        "job",
        "job",
        "event",
        "event",
        "event",
    ]
    times = [item["at"] for item in body["items"]]
    assert times == sorted(times, reverse=True)
    assert body["total"] == 8
    assert body["next_cursor"] is None
    assert "Heat processed" not in _titles(body), "an event about one file is Activity's, not the log's"
    assert "Check watched folders" not in " ".join(item["detail"] or "" for item in body["items"])


def test_every_row_carries_the_record_of_its_source(admin, _seeded) -> None:
    body = _get(admin)

    by_source = {item["source"]: item for item in body["items"]}
    event, job, line = by_source["event"], by_source["job"], by_source["server"]
    assert event["event"]["event_type"] and event["job"] is None and event["server"] is None
    assert job["job"]["id"] and job["event"] is None and job["server"] is None
    assert line["server"]["logger"] and line["event"] is None and line["job"] is None
    failed = next(item for item in body["items"] if item["id"] == f"job:{_seeded['failed_job']}")
    assert failed["level"] == "error"
    assert failed["category"] == "processing"
    assert failed["job"]["last_error"] == "ffmpeg stopped"
    assert failed["workflow"]["id"] == _seeded["library_id"]
    assert failed["workflow"]["name"]


def test_counts_are_what_each_choice_would_show_with_the_other_filters_applied(admin) -> None:
    body = _get(admin, source="event", level="error")

    assert body["total"] == 1
    assert _titles(body) == ["Sign-in failed"]
    assert body["counts"]["source"] == {"event": 1, "job": 1, "server": 1}
    assert body["counts"]["level"]["error"] == 1
    assert body["counts"]["level"]["success"] == 2
    assert body["counts"]["category"]["sign_in"] == 1
    assert set(body["counts"]["category"]) == {
        "processing",
        "scans",
        "cleanup",
        "library",
        "connections",
        "backups",
        "sign_in",
        "updates",
        "weir",
    }


def test_workflow_counts_are_what_choosing_a_workflow_would_show_with_the_other_filters_applied(admin, _seeded) -> None:
    library = str(_seeded["library_id"])

    assert _get(admin)["counts"]["workflow"] == {library: 3}
    assert _get(admin, workflow=library)["counts"]["workflow"] == {library: 3}, "the workflow's own filter is left out"
    assert _get(admin, level="error")["counts"]["workflow"] == {library: 1}
    assert _get(admin, source="server")["counts"]["workflow"] == {}, "server lines belong to no workflow"


def test_the_filters_narrow_every_source_together(admin, _seeded) -> None:
    errors = _get(admin, level="error")
    cleanup = _get(admin, category="cleanup")
    backups = _get(admin, q="backup folder")
    workflow = _get(admin, workflow=_seeded["library_id"])
    several = _get(admin, level="error,warning")

    assert {item["source"] for item in errors["items"]} == {"event", "job", "server"}
    assert all(item["level"] == "error" for item in errors["items"])
    assert [item["source"] for item in cleanup["items"]] == ["job"]
    assert [item["source"] for item in backups["items"]] == ["server"]
    assert {item["source"] for item in workflow["items"]} == {"event", "job"}
    assert all(item["workflow"]["id"] == _seeded["library_id"] for item in workflow["items"])
    assert {item["level"] for item in several["items"]} == {"error", "warning"}


def test_a_filter_only_some_sources_have_leaves_the_others_out(admin) -> None:
    assert {item["source"] for item in _get(admin, trigger="manual")["items"]} == {"event"}
    assert {item["source"] for item in _get(admin, status="failed")["items"]} == {"job"}
    assert {item["source"] for item in _get(admin, has_exception="true")["items"]} == {"server"}


def test_everything_about_one_job_is_its_row_and_the_lines_written_while_it_ran(admin, _seeded) -> None:
    body = _get(admin, job=_seeded["failed_job"])

    assert sorted(item["source"] for item in body["items"]) == ["job", "server"]


def test_a_finished_routine_scan_is_left_out_unless_its_status_is_asked_for(admin) -> None:
    assert len(_get(admin, source="job")["items"]) == 3
    completed = _get(admin, status="completed")
    assert {item["job"]["job_kind"] for item in completed["items"]} == {
        "processing.work_temp_stale_sweep.v1",
        "processing.watched_folder.remux_scan_dispatch.v1",
    }


def test_a_cursor_walks_every_row_exactly_once(admin) -> None:
    everything = [item["id"] for item in _get(admin, limit=100)["items"]]
    walked: list[str] = []
    cursor: str | None = None
    for _ in range(20):
        page = _get(admin, limit=3, **({"cursor": cursor} if cursor else {}))
        walked += [item["id"] for item in page["items"]]
        assert page["total"] == len(everything)
        cursor = page["next_cursor"]
        if cursor is None:
            break

    assert walked == everything


@pytest.mark.parametrize(
    "params",
    [
        {"source": "disk"},
        {"level": "fatal"},
        {"category": "misc"},
        {"status": "stuck"},
        {"limit": 0},
        {"limit": 101},
        {"workflow": 0},
        {"from": "yesterday"},
        {"cursor": "nonsense"},
        {"result": "great"},
        {"has_exception": "maybe"},
    ],
)
def test_a_filter_the_log_does_not_understand_is_refused(admin, params: dict[str, Any]) -> None:
    r = admin.get(LOG, params=params)

    assert r.status_code == 422, r.text
    assert r.json()["detail"]


def test_a_viewer_can_read_the_log(viewer) -> None:
    r = viewer.get(LOG, params=seeded.WINDOW)

    assert r.status_code == 200
    assert r.json()["total"] == 8


def test_the_log_exports_as_a_spreadsheet_or_as_json_for_the_same_filters(admin) -> None:
    as_csv = admin.get(EXPORT, params={**seeded.WINDOW, "level": "error", "format": "csv"})
    as_json = admin.get(EXPORT, params={**seeded.WINDOW, "source": "job", "format": "json"})

    assert as_csv.status_code == 200, as_csv.text
    assert "attachment" in as_csv.headers["content-disposition"]
    rows = list(csv.DictReader(io.StringIO(as_csv.text)))
    assert [row["source"] for row in rows] == ["server", "job", "event"]
    assert {row["level"] for row in rows} == {"error"}
    assert as_json.status_code == 200
    assert [row["source"] for row in as_json.json()] == ["job", "job", "job"]
    assert admin.get(EXPORT, params={"format": "xml"}).status_code == 422


def test_the_three_lists_the_log_replaces_are_still_served(admin) -> None:
    assert admin.get(f"{API}/activity/recent", params={"about": "weir"}).status_code == 200
    assert admin.get(f"{API}/processing/jobs/inspection").status_code == 200
    assert admin.get(f"{API}/suite/logs").status_code == 200
