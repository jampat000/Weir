"""The System view's readings: ``GET /system/stats`` and the ``system.stats`` frame on the Activity stream.

The server reads the machine it runs on, so these tests judge shapes, types and ranges, never the numbers.
"""

from __future__ import annotations

from datetime import datetime
from pathlib import Path
from typing import Any

from tests.contract.support.client import API, WeirClient
from tests.contract.support.polling import wait_until
from tests.contract.support.sse import open_stream

STATS = f"{API}/system/stats"
LIBRARIES = f"{API}/processing/libraries"

NOW_KEYS = {
    "at",
    "cpu_percent",
    "cores",
    "memory_used_bytes",
    "memory_total_bytes",
    "disk_read_bytes_per_sec",
    "disk_write_bytes_per_sec",
    "disk_busy_percent",
    "weir_cpu_percent",
    "weir_memory_bytes",
    "tools_cpu_percent",
    "processing_read_bytes_per_sec",
    "processing_write_bytes_per_sec",
    "processing_speed",
    "running",
    "slots",
}
POINT_KEYS = {
    "at",
    "cpu_percent",
    "memory_percent",
    "disk_read_bytes_per_sec",
    "disk_write_bytes_per_sec",
    "processing_read_bytes_per_sec",
    "processing_write_bytes_per_sec",
    "processing_speed",
}
MACHINE_KEYS = {"os", "uptime_seconds", "reboot_pending"}
DRIVE_KEYS = {
    "name",
    "path",
    "total_bytes",
    "free_bytes",
    "weir_bytes",
    "keep_free_bytes",
    "full_in_days",
    "read_bytes_per_sec",
    "write_bytes_per_sec",
    "busy_percent",
    "workflows",
}
PERCENT_KEYS = {"cpu_percent", "memory_percent", "disk_busy_percent", "weir_cpu_percent", "tools_cpu_percent"}

# Drives are read every 30 seconds, so a workflow created now shows on its drive within about that long.
DRIVE_READ_WAIT_S = 50.0


def _assert_reading_is_sane(values: dict[str, Any]) -> None:
    """A figure is a non-negative number, or null where the machine could not be read; a percentage is at most 100."""

    for key, value in values.items():
        if key == "at":
            assert isinstance(value, str)
        elif value is not None:
            assert isinstance(value, int | float), (key, value)
            assert not isinstance(value, bool), key
            assert value >= 0, (key, value)
            if key in PERCENT_KEYS:
                assert value <= 100, (key, value)


def _moment(text: str) -> datetime:
    return datetime.fromisoformat(text)


def _stats(admin: WeirClient) -> dict[str, Any]:
    r = admin.get(STATS)
    assert r.status_code == 200, r.text
    return r.json()


def test_system_stats_require_authentication(client: WeirClient) -> None:
    assert client.get(STATS).status_code == 401


def test_system_stats_have_the_documented_shape(admin: WeirClient) -> None:
    body = _stats(admin)

    assert set(body) == {"interval_ms", "window_s", "now", "history", "machine", "drives"}
    assert body["interval_ms"] == 1000
    assert body["window_s"] == 600
    assert set(body["now"]) == NOW_KEYS
    _assert_reading_is_sane(body["now"])
    assert body["now"]["cores"] >= 1
    assert body["now"]["slots"] >= 0
    assert body["now"]["running"] >= 0
    assert set(body["machine"]) == MACHINE_KEYS


def test_the_history_has_a_time_on_every_point_oldest_first(admin: WeirClient) -> None:
    body = _stats(admin)
    history = body["history"]

    assert 1 <= len(history) <= 600
    for point in history:
        assert set(point) == POINT_KEYS
        _assert_reading_is_sane(point)
    times = [_moment(point["at"]) for point in history]
    assert times == sorted(times)
    # The newest point is the reading the response was taken from.
    assert history[-1]["at"] == body["now"]["at"]


def test_the_machine_says_what_it_can(admin: WeirClient) -> None:
    machine = _stats(admin)["machine"]

    assert machine["os"] is None or isinstance(machine["os"], str)
    assert machine["uptime_seconds"] is None or machine["uptime_seconds"] >= 0
    assert machine["reboot_pending"] is None or isinstance(machine["reboot_pending"], bool)


def test_every_drive_that_holds_a_workflow_folder_is_listed_with_its_workflows(admin: WeirClient) -> None:
    def seeded_work_folders_listed() -> list[dict[str, Any]] | None:
        drives = _stats(admin)["drives"]
        return drives or None

    drives = wait_until(seeded_work_folders_listed, timeout_s=DRIVE_READ_WAIT_S, what="the first drive reading")

    for drive in drives:
        assert set(drive) == DRIVE_KEYS
        assert drive["total_bytes"] >= drive["free_bytes"] >= 0
        assert drive["weir_bytes"] >= 0
        assert drive["keep_free_bytes"] >= 0
        assert drive["name"]
        assert drive["workflows"], "a drive is listed only because a workflow keeps a folder on it"
        for workflow in drive["workflows"]:
            assert set(workflow) == {"id", "name", "roles"}
            assert workflow["roles"]
            assert set(workflow["roles"]) <= {"watched", "work", "output"}
    # The seeded workflows keep their work files under Weir's own folder, so they are on a drive.
    on_a_drive = {workflow["name"] for drive in drives for workflow in drive["workflows"]}
    assert {"Movies", "TV"} <= on_a_drive


def test_a_new_workflows_folders_appear_on_their_drive_and_ask_for_the_free_space_it_wants(
    admin: WeirClient, tmp_path: Path
) -> None:
    watched, output = tmp_path / "in", tmp_path / "out"
    watched.mkdir()
    output.mkdir()
    created = admin.post_csrf(
        LIBRARIES,
        {
            "enabled": False,
            "name": "Stats Probe",
            "media_type": "movie",
            "watched_folder": str(watched),
            "output_folder": str(output),
            "minimum_free_disk_space_mb": 2048,
        },
    )
    assert created.status_code in (200, 201), created.text

    def probe_drive() -> dict[str, Any] | None:
        for drive in _stats(admin)["drives"]:
            for workflow in drive["workflows"]:
                if workflow["name"] == "Stats Probe":
                    return {"drive": drive, "workflow": workflow}
        return None

    found = wait_until(probe_drive, timeout_s=DRIVE_READ_WAIT_S, what="the new workflow's drive")

    assert {"watched", "output"} <= set(found["workflow"]["roles"])
    assert found["drive"]["keep_free_bytes"] >= 2048 * 1024 * 1024


def test_the_activity_stream_carries_system_stats_frames_a_second_apart(server, admin: WeirClient) -> None:
    with open_stream(server, admin) as stream:
        first = stream.next_event_named("system.stats")
        second = stream.next_event_named("system.stats")

    for frame in (first, second):
        assert set(frame) == {"now", "point"}
        assert set(frame["now"]) == NOW_KEYS
        assert set(frame["point"]) == POINT_KEYS
        _assert_reading_is_sane(frame["now"])
        _assert_reading_is_sane(frame["point"])
        assert frame["point"]["at"] == frame["now"]["at"]
    assert _moment(second["now"]["at"]) > _moment(first["now"]["at"])


def test_system_stats_are_documented_in_the_openapi_schema(client: WeirClient) -> None:
    schema = client.get("/openapi.json").json()

    assert "/api/v1/system/stats" in schema["paths"]
    schemas = schema["components"]["schemas"]
    assert set(schemas["SystemStatsNowOut"]["properties"]) == NOW_KEYS
    assert set(schemas["SystemStatsPointOut"]["properties"]) == POINT_KEYS
    assert set(schemas["SystemStatsFrame"]["properties"]) == {"now", "point"}


def test_system_stats_only_answer_get(admin: WeirClient) -> None:
    assert admin.post(STATS).status_code == 405
