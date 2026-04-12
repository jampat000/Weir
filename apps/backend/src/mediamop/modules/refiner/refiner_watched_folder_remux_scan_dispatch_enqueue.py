"""Enqueue ``refiner.watched_folder.remux_scan_dispatch.v1`` (manual HTTP + Refiner-local periodic timer)."""

from __future__ import annotations

import json
from uuid import uuid4

from sqlalchemy import func, select
from sqlalchemy.orm import Session

from mediamop.core.config import MediaMopSettings
from mediamop.modules.refiner.jobs_model import RefinerJob, RefinerJobStatus
from mediamop.modules.refiner.jobs_ops import refiner_enqueue_or_get_job
from mediamop.modules.refiner.refiner_path_settings_service import ensure_refiner_path_settings_row
from mediamop.modules.refiner.refiner_watched_folder_remux_scan_dispatch_job_kinds import (
    REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
)


def refiner_watched_folder_remux_scan_dispatch_queue_has_active_scan(session: Session) -> bool:
    """True when a scan job is already ``pending`` or ``leased`` (scan-level duplicate guard)."""

    n = session.scalar(
        select(func.count())
        .select_from(RefinerJob)
        .where(
            RefinerJob.job_kind == REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
            RefinerJob.status.in_(
                (
                    RefinerJobStatus.PENDING.value,
                    RefinerJobStatus.LEASED.value,
                ),
            ),
        ),
    )
    return int(n or 0) > 0


def validate_watched_folder_scan_dispatch_prerequisites(
    session: Session,
    *,
    enqueue_remux_jobs: bool,
    remux_dry_run: bool,
) -> tuple[bool, str | None]:
    """Shared checks for manual HTTP and periodic enqueue (path row; watched; live remux output)."""

    row = ensure_refiner_path_settings_row(session)
    if not (row.refiner_watched_folder or "").strip():
        return False, "no_saved_watched_folder"
    if enqueue_remux_jobs and not remux_dry_run and not (row.refiner_output_folder or "").strip():
        return False, "missing_output_for_live_remux"
    return True, None


def enqueue_watched_folder_remux_scan_dispatch_job(
    session: Session,
    *,
    enqueue_remux_jobs: bool,
    remux_dry_run: bool,
    scan_trigger: str,
) -> RefinerJob:
    """Insert one scan job (unique ``dedupe_key``). Caller must commit."""

    payload = {
        "enqueue_remux_jobs": enqueue_remux_jobs,
        "remux_dry_run": remux_dry_run,
        "scan_trigger": scan_trigger,
    }
    dedupe_key = f"{REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND}:{uuid4().hex}"
    return refiner_enqueue_or_get_job(
        session,
        dedupe_key=dedupe_key,
        job_kind=REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
        payload_json=json.dumps(payload, separators=(",", ":")),
    )


def try_enqueue_periodic_watched_folder_remux_scan_dispatch(
    session: Session,
    settings: MediaMopSettings,
) -> tuple[bool, str | None]:
    """Periodic tick: enqueue at most one new scan when idle and prerequisites pass.

    Returns ``(inserted, skip_reason)`` where ``skip_reason`` is a short machine token or ``None``.
    """

    if not settings.refiner_watched_folder_remux_scan_dispatch_schedule_enabled:
        return False, "schedule_disabled"
    if refiner_watched_folder_remux_scan_dispatch_queue_has_active_scan(session):
        return False, "active_scan_already_queued"
    ok, err = validate_watched_folder_scan_dispatch_prerequisites(
        session,
        enqueue_remux_jobs=settings.refiner_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs,
        remux_dry_run=settings.refiner_watched_folder_remux_scan_dispatch_periodic_remux_dry_run,
    )
    if not ok:
        return False, err
    enqueue_watched_folder_remux_scan_dispatch_job(
        session,
        enqueue_remux_jobs=settings.refiner_watched_folder_remux_scan_dispatch_periodic_enqueue_remux_jobs,
        remux_dry_run=settings.refiner_watched_folder_remux_scan_dispatch_periodic_remux_dry_run,
        scan_trigger="periodic",
    )
    return True, None
