"""#540 item 1: a job that runs longer than its lease must never be claimed by a second worker while
the first is still running it.

``WEIR_PROCESSING_JOB_LEASE_SECONDS`` shortens the lease (30 s is the shortest the server accepts), and
a heartbeat renews it roughly every ``lease_seconds / 3`` while the handler runs. The heartbeat itself
is unit-tested in ``Weir.Infrastructure.Tests/Jobs/LeaseRenewalTests.cs``; this test proves it over a
real hand-off with two workers.
"""

from __future__ import annotations

from pathlib import Path

from tests.contract.processing import _helpers as h
from tests.contract.support.fake_ffmpeg import fake_media_bytes, probe


def test_a_job_longer_than_its_lease_is_never_claimed_twice(
    server_factory, client_factory, fake_ffmpeg, fake_managers, tmp_path: Path
) -> None:
    # Two workers sharing one database; a remux that runs long enough to outlive a short lease
    # must still only ever be claimed by one of them. 30s is the minimum WEIR_PROCESSING_JOB_LEASE_SECONDS
    # accepts (WeirOptionsLoader.ClampProcessingJobLeaseSeconds); the heartbeat renews it about every 10s,
    # so a 45s remux leaves a comfortable window (past the 5s idle poll) where a second worker would
    # have claimed the row already if renewal were broken.
    server = server_factory(
        env={
            **h.working_env(fake_ffmpeg),
            "WEIR_PROCESSING_WORKER_COUNT": "2",
            "WEIR_PROCESSING_JOB_LEASE_SECONDS": "30",
        }
    )
    admin = client_factory(server)
    admin.ensure_admin()
    folders = h.Folders.make(tmp_path)
    _fake, _library = h.deluno_setup(admin, fake_managers, folders)
    # Deliberately longer than the lease, so a heartbeat is required to keep the lease alive for the
    # whole run; the fake ffmpeg reports its steps as it goes either way.
    fake_ffmpeg.set_file_rule("film.mkv", probe=probe(), remux_delay_seconds=45)
    release = folders.watched / "Long.Running.540"
    release.mkdir()
    source = release / "film.mkv"
    source.write_bytes(fake_media_bytes(probe()))

    h.post_handoff(admin, handoff_id="handoff-lease-540", source_path=source)
    h.wait_for_handoff_state(admin, "handoff-lease-540", "completed", timeout_s=120)

    # Only one remux call for this file must ever have started: a second worker claiming the same
    # row mid-write would show up here as two "remux" calls for film.mkv instead of one.
    assert len(h.jobs(admin, kind=h.REMUX_KIND)) == 1
    assert len(fake_ffmpeg.calls(tool="ffmpeg", step="remux")) == 1
