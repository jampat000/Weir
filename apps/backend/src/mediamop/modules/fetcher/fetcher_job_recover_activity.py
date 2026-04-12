"""Activity trail for manual ``handler_ok_finalize_failed`` → ``completed`` recovery on ``fetcher_jobs``."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.modules.fetcher.failed_import_drive_job_kinds import FETCHER_FAILED_IMPORT_DRIVE_JOB_KINDS
from mediamop.modules.fetcher.fetcher_job_operator_labels import fetcher_job_kind_operator_label
from mediamop.platform.activity import constants as C
from mediamop.platform.activity.service import record_activity_event


def record_fetcher_job_handler_ok_finalize_recovered(
    db: Session,
    *,
    job_id: int,
    job_kind: str,
) -> None:
    """Persist one activity row; failed-import drive kinds keep the historical failed-import event type."""

    label = fetcher_job_kind_operator_label(job_kind)
    if job_kind in FETCHER_FAILED_IMPORT_DRIVE_JOB_KINDS:
        record_activity_event(
            db,
            event_type=C.FETCHER_FAILED_IMPORT_RECOVERED,
            module="fetcher",
            title="Fetcher recovery marked a failed-import download-queue job completed (the pass was not re-run).",
            detail=f"Job {job_id} — {label}.",
        )
        return

    record_activity_event(
        db,
        event_type=C.FETCHER_JOB_RECOVERED_HANDLER_OK_FINALIZE,
        module="fetcher",
        title=f'Fetcher marked «{label}» completed after handler_ok_finalize_failed (the handler was not re-run).',
        detail=f"Job {job_id} — kind: {job_kind}.",
    )
