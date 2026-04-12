"""Periodic watched-folder scan dispatch enqueue — Refiner-local duplicate guard + prerequisites."""

from __future__ import annotations

import json
from dataclasses import replace

from sqlalchemy import delete, select, update

from mediamop.core.config import MediaMopSettings
from mediamop.core.db import create_db_engine, create_session_factory
from mediamop.modules.refiner.jobs_model import RefinerJob, RefinerJobStatus
from mediamop.modules.refiner.refiner_path_settings_model import RefinerPathSettingsRow
from mediamop.modules.refiner.refiner_watched_folder_remux_scan_dispatch_enqueue import (
    refiner_watched_folder_remux_scan_dispatch_queue_has_active_scan,
    try_enqueue_periodic_watched_folder_remux_scan_dispatch,
)
from mediamop.modules.refiner.refiner_watched_folder_remux_scan_dispatch_job_kinds import (
    REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
)

import mediamop.modules.refiner.jobs_model  # noqa: F401
import mediamop.modules.refiner.refiner_path_settings_model  # noqa: F401
import mediamop.platform.activity.models  # noqa: F401
import mediamop.platform.auth.models  # noqa: F401


def _fac():
    settings = MediaMopSettings.load()
    eng = create_db_engine(settings)
    return create_session_factory(eng)


def _settings_on() -> MediaMopSettings:
    base = MediaMopSettings.load()
    return replace(
        base,
        refiner_watched_folder_remux_scan_dispatch_schedule_enabled=True,
        refiner_watched_folder_remux_scan_dispatch_schedule_interval_seconds=3600,
        refiner_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs=False,
        refiner_watched_folder_remux_scan_dispatch_periodic_remux_dry_run=True,
    )


def test_queue_has_active_scan_detects_pending_and_leased() -> None:
    fac = _fac()
    with fac() as db:
        db.execute(delete(RefinerJob))
        db.add(
            RefinerJob(
                dedupe_key="wf-scan-active-1",
                job_kind=REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
                status=RefinerJobStatus.PENDING.value,
            ),
        )
        db.commit()
    with fac() as db:
        assert refiner_watched_folder_remux_scan_dispatch_queue_has_active_scan(db) is True

    with fac() as db:
        db.execute(delete(RefinerJob))
        db.add(
            RefinerJob(
                dedupe_key="wf-scan-active-2",
                job_kind=REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
                status=RefinerJobStatus.LEASED.value,
                lease_owner="w",
                attempt_count=1,
            ),
        )
        db.commit()
    with fac() as db:
        assert refiner_watched_folder_remux_scan_dispatch_queue_has_active_scan(db) is True

    with fac() as db:
        db.execute(delete(RefinerJob))
        db.add(
            RefinerJob(
                dedupe_key="wf-scan-done",
                job_kind=REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
                status=RefinerJobStatus.COMPLETED.value,
                attempt_count=1,
            ),
        )
        db.commit()
    with fac() as db:
        assert refiner_watched_folder_remux_scan_dispatch_queue_has_active_scan(db) is False


def test_try_enqueue_periodic_skips_when_schedule_disabled() -> None:
    fac = _fac()
    base = MediaMopSettings.load()
    off = replace(base, refiner_watched_folder_remux_scan_dispatch_schedule_enabled=False)
    with fac() as db:
        ins, skip = try_enqueue_periodic_watched_folder_remux_scan_dispatch(db, off)
        db.rollback()
    assert ins is False
    assert skip == "schedule_disabled"


def test_try_enqueue_periodic_skips_when_active_scan_exists() -> None:
    fac = _fac()
    settings = _settings_on()
    with fac() as db:
        db.execute(delete(RefinerJob))
        db.add(
            RefinerJob(
                dedupe_key="wf-scan-block",
                job_kind=REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
                status=RefinerJobStatus.PENDING.value,
            ),
        )
        db.commit()
    with fac() as db:
        ins, skip = try_enqueue_periodic_watched_folder_remux_scan_dispatch(db, settings)
        db.rollback()
    assert ins is False
    assert skip == "active_scan_already_queued"


def test_try_enqueue_periodic_inserts_when_prerequisites_met(tmp_path) -> None:
    fac = _fac()
    settings = _settings_on()
    watched = str(tmp_path / "watch")
    tmp_path.joinpath("watch").mkdir()
    out = str(tmp_path / "out")
    tmp_path.joinpath("out").mkdir()
    with fac() as db:
        db.execute(delete(RefinerJob))
        db.execute(
            update(RefinerPathSettingsRow)
            .where(RefinerPathSettingsRow.id == 1)
            .values(
                refiner_watched_folder=watched,
                refiner_output_folder=out,
            ),
        )
        db.commit()
    try:
        with fac() as db:
            ins, skip = try_enqueue_periodic_watched_folder_remux_scan_dispatch(db, settings)
            db.commit()
        assert ins is True
        assert skip is None
        with fac() as db:
            row = db.scalars(
                select(RefinerJob).where(RefinerJob.job_kind == REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND),
            ).first()
            assert row is not None
            assert row.status == RefinerJobStatus.PENDING.value
            body = json.loads(row.payload_json or "{}")
            assert body.get("scan_trigger") == "periodic"
    finally:
        with fac() as db:
            db.execute(delete(RefinerJob))
            db.execute(
                update(RefinerPathSettingsRow)
                .where(RefinerPathSettingsRow.id == 1)
                .values(
                    refiner_watched_folder=None,
                    refiner_output_folder="",
                ),
            )
            db.commit()
