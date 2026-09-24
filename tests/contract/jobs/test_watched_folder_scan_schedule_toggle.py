"""#533: the switch that turns off periodic watched-folder scans
(``WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED``) must stop the scan timer, while
a manual scan still works.

The scheduler (``ProcessingWatchedFolderScanDispatchScheduleTask``) reads the switch on every tick. Its
timing is unit-tested with a controlled clock in
``Weir.Infrastructure.Tests.Jobs.ProcessingWatchedFolderScanDispatchScheduleTaskTests``; these tests check
it from outside, with a positive control so a quiet timer cannot pass for a working switch.

The switched-off case used to be watched for two intervals rather than read from the API, because nothing
reported the scheduler's own state. #747 closed that gap: a library's ``periodic_scan`` field
(``off`` / ``outside_hours`` / ``scheduled``) and ``next_scan_at`` are computed straight from the same
switches and schedule window the scheduler and the worker's upkeep admission read (see
``PeriodicScanStatus`` in ``Weir.Core.Processing``), so the switched-off case is read immediately instead
of waited for.
"""

from __future__ import annotations

from pathlib import Path

from tests.contract.jobs._helpers import job_by_id, library_for_scope, save_library
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.launcher import ServerUnderTest
from tests.contract.support.polling import wait_until

SCAN_KIND = "processing.watched_folder.remux_scan_dispatch.v1"
ENQUEUE = f"{API}/processing/jobs/watched-folder-remux-scan-dispatch/enqueue"
# The scheduler clamps every library's cadence to at least 10s (PeriodicSchedule.WatchedFolderScanIntervalSeconds).
SHORT_INTERVAL_SECONDS = 10


def _scan_job_count(admin: WeirClient) -> int:
    r = admin.get(f"{API}/processing/jobs/inspection", params={"limit": 100})
    assert r.status_code == 200, r.text
    return sum(1 for job in r.json()["jobs"] if job["job_kind"] == SCAN_KIND)


def test_disabling_the_periodic_scan_switch_reports_it_off(server_factory, client_factory, tmp_path: Path) -> None:
    watched = tmp_path / "watched"
    watched.mkdir()
    output = tmp_path / "output"
    output.mkdir()
    sut: ServerUnderTest = server_factory(
        env={"WEIR_PROCESSING_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_SCHEDULE_ENABLED": "0"}
    )
    admin = client_factory(sut)
    admin.ensure_admin()
    movie = library_for_scope(admin, "movie")
    saved = save_library(
        admin,
        movie["id"],
        watched_folder=str(watched),
        output_folder=str(output),
        scan_interval_seconds=SHORT_INTERVAL_SECONDS,
        skip_access_tests=True,
    )

    assert saved["periodic_scan"] == "off"
    assert saved["next_scan_at"] is None

    # A manual scan must still work regardless of the periodic switch.
    manual = admin.post_csrf(ENQUEUE, {"enqueue_remux_jobs": False})
    assert manual.status_code == 200, manual.text
    job = job_by_id(admin, manual.json()["job_id"])
    assert job["job_kind"] == SCAN_KIND


def test_periodic_scan_switch_left_on_lets_the_scan_timer_run(server_factory, client_factory, tmp_path: Path) -> None:
    """Positive control for the test above: with the switch untouched (on by default), the same
    library, on the same short cadence, must eventually get a periodic scan — proving the negative
    result above is the switch actually working, not the timer coincidentally never firing in time."""

    watched = tmp_path / "watched"
    watched.mkdir()
    output = tmp_path / "output"
    output.mkdir()
    sut: ServerUnderTest = server_factory()
    admin = client_factory(sut)
    admin.ensure_admin()
    movie = library_for_scope(admin, "movie")
    saved = save_library(
        admin,
        movie["id"],
        watched_folder=str(watched),
        output_folder=str(output),
        scan_interval_seconds=SHORT_INTERVAL_SECONDS,
        skip_access_tests=True,
    )

    assert saved["periodic_scan"] == "scheduled"

    # Restart so the scheduler's first tick sees the library already configured, rather than racing
    # this test's own setup calls and locking in a stale "not ready yet" due time for a full interval
    # (that race is real: the scheduler can queue its first attempt almost as soon as the process is up,
    # which is often before this test's own HTTP setup calls above land).
    with seed.stopped(sut):
        pass
    admin = client_factory(sut)
    admin.ensure_admin()

    # No manual scan preceded this, so any scan-dispatch job at all here is the periodic timer's doing —
    # RunAtStart can fire it before this line even runs, so this checks for one rather than a delta.
    wait_until(
        lambda: _scan_job_count(admin) > 0,
        timeout_s=SHORT_INTERVAL_SECONDS + 20,
        interval_s=0.5,
        what="a periodic scan-dispatch job with the switch on",
    )
