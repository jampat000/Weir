"""What System shows: the overview, the scheduled tasks, and the ``system.tasks`` and ``system.log`` frames."""

from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Any

import pytest

from tests.contract.jobs._helpers import insert_job, set_movie_folders
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.polling import wait_until
from tests.contract.support.sse import SseReader, open_stream

OVERVIEW = f"{API}/system/overview"
TASKS = f"{API}/system/tasks"
OVERVIEW_FIELDS = [
    "version",
    "update",
    "uptime_seconds",
    "started_at",
    "runs_as",
    "address",
    "data_bytes",
    "browsers_live",
    "requests",
    "jobs_today",
    "restarts_this_week",
    "checks",
]
TASK_FIELDS = ["key", "label", "running", "last_run_at", "last_ok", "last_error", "next_run_at", "interval_seconds"]
UPDATE_STATUSES = {"checking", "up_to_date", "update_available", "downloaded", "not_published", "unavailable"}
REMUX_PASS = "processing.file.remux_pass.v1"
SCAN_DISPATCH = "processing.watched_folder.remux_scan_dispatch.v1"
# The in-process workers, and the cleanup timers, which the shared server leaves off.
WORKERS_ON = {"WEIR_PROCESSING_WORKER_COUNT": "1"}
CLEANUP_TIMERS_ON = {
    **WORKERS_ON,
    "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_MOVIE_SCHEDULE_ENABLED": "1",
    "WEIR_PROCESSING_WORK_TEMP_STALE_SWEEP_TV_SCHEDULE_ENABLED": "1",
}


def _at(moment: str) -> datetime:
    return datetime.fromisoformat(moment.replace("Z", "+00:00"))


def _overview(client: WeirClient) -> dict[str, Any]:
    response = client.get(OVERVIEW)
    assert response.status_code == 200, response.text
    return response.json()


def _tasks(client: WeirClient) -> list[dict[str, Any]]:
    response = client.get(TASKS)
    assert response.status_code == 200, response.text
    return response.json()


def _opened(stream: SseReader) -> None:
    """Read the frames every stream opens with, so it is certainly listening when the test acts."""

    assert stream.next_block() == ["retry: 5000"]
    stream.next_event_named("activity.latest")


@pytest.mark.parametrize("path", [OVERVIEW, TASKS])
def test_both_endpoints_need_a_signed_in_user(client: WeirClient, path: str) -> None:
    assert client.get(path).status_code == 401


def test_the_overview_has_the_documented_fields_and_types(admin: WeirClient) -> None:
    overview = _overview(admin)

    assert list(overview) == OVERVIEW_FIELDS
    assert isinstance(overview["version"], str) and overview["version"]
    assert list(overview["update"]) == ["status", "latest_version"]
    assert overview["update"]["status"] in UPDATE_STATUSES
    assert isinstance(overview["uptime_seconds"], int) and overview["uptime_seconds"] >= 0
    assert overview["started_at"].endswith("Z")
    assert overview["runs_as"] in {"service", "app", "docker"}
    assert isinstance(overview["data_bytes"], int) and overview["data_bytes"] > 0
    assert list(overview["requests"]) == ["median_ms", "p95_ms", "errors_today"]
    assert overview["requests"]["median_ms"] <= overview["requests"]["p95_ms"]
    assert list(overview["jobs_today"]) == ["run", "failed"]
    assert list(overview["checks"]) == ["passing", "total"]
    assert 0 <= overview["checks"]["passing"] <= overview["checks"]["total"]


def test_the_overview_says_where_this_weir_listens(server, admin: WeirClient) -> None:
    # The contract server listens on 127.0.0.1 only, which a browser reaches as localhost.
    assert _overview(admin)["address"] == f"http://localhost:{server.port}"


def test_a_server_started_by_hand_runs_as_an_app(admin: WeirClient) -> None:
    assert _overview(admin)["runs_as"] == "app"


def test_the_overview_counts_every_browser_holding_the_stream_open(server, admin: WeirClient) -> None:
    assert _overview(admin)["browsers_live"] == 0

    with open_stream(server, admin) as first:
        _opened(first)
        assert _overview(admin)["browsers_live"] == 1
        with open_stream(server, admin) as second:
            _opened(second)
            assert _overview(admin)["browsers_live"] == 2

    wait_until(lambda: _overview(admin)["browsers_live"] == 0, what="the closed streams to stop counting")


def test_uptime_and_restarts_follow_the_server_through_a_restart(server_factory, client_factory) -> None:
    sut = server_factory()
    admin = client_factory(sut)
    admin.ensure_admin()
    first = _overview(admin)
    assert first["restarts_this_week"] == 0

    sut.restart()
    admin = client_factory(sut)
    admin.login()
    second = _overview(admin)

    assert second["restarts_this_week"] == 1
    assert _at(second["started_at"]) > _at(first["started_at"])
    assert second["uptime_seconds"] < 120


def test_jobs_today_counts_the_jobs_that_finished_and_the_ones_that_failed(server_factory, client_factory) -> None:
    sut = server_factory()
    admin = client_factory(sut)
    admin.ensure_admin()
    before = _overview(admin)["jobs_today"]
    with seed.stopped(sut) as conn:
        insert_job(conn, dedupe_key="overview-1", job_kind=REMUX_PASS, status="completed")
        insert_job(conn, dedupe_key="overview-2", job_kind=REMUX_PASS, status="completed")
        insert_job(conn, dedupe_key="overview-3", job_kind=REMUX_PASS, status="failed")
        # A scan that found nothing wrong is left out, as it is from the Jobs list.
        insert_job(conn, dedupe_key="overview-4", job_kind=SCAN_DISPATCH, status="completed")
        insert_job(conn, dedupe_key="overview-5", job_kind=REMUX_PASS, status="pending")
    admin = client_factory(sut)
    admin.login()

    after = _overview(admin)["jobs_today"]

    assert (after["run"] - before["run"], after["failed"] - before["failed"]) == (3, 1)


def test_the_tasks_list_has_each_task_with_its_last_run_and_its_next(admin: WeirClient) -> None:
    def log_retention_has_run() -> dict[str, Any] | None:
        return next((task for task in _tasks(admin) if task["key"] == "suite-log-retention" and task["last_ok"]), None)

    log = wait_until(log_retention_has_run, what="the log retention task to finish its first run")
    tasks = _tasks(admin)

    assert all(list(task) == TASK_FIELDS for task in tasks)
    assert (log["label"], log["running"], log["last_error"], log["interval_seconds"]) == (
        "Trim the log",
        False,
        None,
        3600,
    )
    assert log["last_run_at"].endswith("Z") and _at(log["next_run_at"]) > _at(log["last_run_at"])
    assert {task["label"] for task in tasks} >= {"Scan Movies", "Scan TV", "Tidy finished jobs", "Clear old sign-ins"}
    # Plumbing that polls every few seconds is not listed.
    assert not {"activity-latest-poll", "connection-usage-flush", "artwork-resolver"} & {task["key"] for task in tasks}


def test_a_switched_on_cleanup_is_listed_with_its_label_and_how_often_it_runs(server_factory, client_factory) -> None:
    sut = server_factory(env=CLEANUP_TIMERS_ON)
    admin = client_factory(sut)
    admin.ensure_admin()

    cleanup = wait_until(
        lambda: next((t for t in _tasks(admin) if t["key"] == "cleanup-leftover-files" and t["last_ok"]), None),
        what="the cleanup to finish its first run",
    )

    assert (cleanup["label"], cleanup["interval_seconds"], cleanup["last_error"]) == (
        "Clear leftover files",
        3600,
        None,
    )
    assert _at(cleanup["next_run_at"]) > _at(cleanup["last_run_at"])
    # The cleanup for unclaimed copies stays off until a person switches it on.
    assert "cleanup-unclaimed-copies" not in {task["key"] for task in _tasks(admin)}


def test_a_scan_that_runs_reaches_an_open_stream_as_a_system_tasks_frame(
    server_factory, client_factory, tmp_path: Path
) -> None:
    sut = server_factory(env=WORKERS_ON)
    admin = client_factory(sut)
    admin.ensure_admin()
    watched = tmp_path / "watched"
    watched.mkdir()
    output = tmp_path / "output"
    output.mkdir()
    library_id = set_movie_folders(admin, watched=str(watched.resolve()), output=str(output.resolve()))
    scan = f"scan-{library_id}"
    wait_until(lambda: any(task["key"] == scan for task in _tasks(admin)), what="the workflow's scan to be listed")
    before = next(task for task in _tasks(admin) if task["key"] == scan)["last_run_at"]

    with open_stream(sut, admin) as stream:
        _opened(stream)
        queued = admin.post_csrf(f"{API}/processing/jobs/watched-folder-remux-scan-dispatch/enqueue", json={})
        assert queued.status_code == 200, queued.text
        while True:
            frame = stream.next_event_named("system.tasks")
            task = next((task for task in frame if task["key"] == scan), None)
            if (
                task
                and not task["running"]
                and task["last_run_at"]
                and (before is None or _at(task["last_run_at"]) > _at(before))
            ):
                break

    assert (task["label"], task["last_ok"], task["last_error"]) == ("Scan Movies", True, None)
    assert all(list(entry) == TASK_FIELDS for entry in frame)


def test_a_warning_the_server_logs_reaches_an_open_stream_as_a_system_log_frame(
    server, admin: WeirClient, client_factory
) -> None:
    with open_stream(server, admin) as stream:
        _opened(stream)
        client_factory(server).login(password="not-the-password", expect=401)

        frame = stream.next_event_named("system.log")

    assert list(frame) == ["at", "level", "message"]
    assert frame["level"] == "WARNING"
    assert frame["message"] == "auth event: login failed"
    assert frame["at"].endswith("Z")
