"""Read-only ``refiner_jobs`` listing for operators (Refiner lane; not a cross-module framework)."""

from __future__ import annotations

from sqlalchemy import select
from sqlalchemy.orm import Session

from mediamop.modules.refiner.jobs_model import RefinerJob, RefinerJobStatus
from mediamop.modules.refiner.schemas_refiner_jobs_inspection import (
    RefinerJobInspectionRow,
    RefinerJobsInspectionOut,
)

_ALLOWED_STATUS: frozenset[str] = frozenset(s.value for s in RefinerJobStatus)


def validate_refiner_inspection_statuses(statuses: tuple[str, ...]) -> tuple[str, ...]:
    """Validate user-supplied status strings against persisted enum values."""

    unknown = [s for s in statuses if s not in _ALLOWED_STATUS]
    if unknown:
        msg = f"Invalid status filter values: {unknown!r}; allowed={sorted(_ALLOWED_STATUS)}"
        raise ValueError(msg)
    return statuses


def list_refiner_jobs_for_inspection(
    session: Session,
    *,
    limit: int,
    statuses: tuple[str, ...] | None,
) -> RefinerJobsInspectionOut:
    """Return up to ``limit`` rows, ``updated_at`` descending.

    When ``statuses`` is empty/None, returns the most recently touched rows **across all
    statuses** (Refiner queue is expected to stay small; operators need pending/leased visibility
    without repeating ``status=`` for every state).
    """

    if statuses:
        stmt = (
            select(RefinerJob)
            .where(RefinerJob.status.in_(statuses))
            .order_by(RefinerJob.updated_at.desc())
            .limit(limit)
        )
        default_recent_slice = False
    else:
        stmt = select(RefinerJob).order_by(RefinerJob.updated_at.desc()).limit(limit)
        default_recent_slice = True
    rows = session.scalars(stmt).all()
    return RefinerJobsInspectionOut(
        jobs=[RefinerJobInspectionRow.model_validate(r) for r in rows],
        default_recent_slice=default_recent_slice,
    )
