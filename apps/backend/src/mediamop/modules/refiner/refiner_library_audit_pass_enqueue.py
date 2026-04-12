"""Enqueue durable Refiner library audit pass rows on ``refiner_jobs``."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.modules.refiner.jobs_model import RefinerJob
from mediamop.modules.refiner.jobs_ops import refiner_enqueue_or_get_job
from mediamop.modules.refiner.refiner_library_audit_pass_job_kinds import (
    REFINER_LIBRARY_AUDIT_PASS_DEDUPE_KEY,
    REFINER_LIBRARY_AUDIT_PASS_JOB_KIND,
)


def enqueue_refiner_library_audit_pass_job(session: Session) -> RefinerJob:
    """Insert or return the singleton library audit pass row (dedupe key is family-owned)."""

    return refiner_enqueue_or_get_job(
        session,
        dedupe_key=REFINER_LIBRARY_AUDIT_PASS_DEDUPE_KEY,
        job_kind=REFINER_LIBRARY_AUDIT_PASS_JOB_KIND,
        payload_json="{}",
    )
