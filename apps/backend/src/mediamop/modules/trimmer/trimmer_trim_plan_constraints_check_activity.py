"""Activity writes for ``trimmer.trim_plan.constraints_check.v1``."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.platform.activity import constants as C
from mediamop.platform.activity.service import record_activity_event


def record_trimmer_trim_plan_constraints_check_completed(
    db: Session,
    *,
    detail: str | None,
) -> None:
    record_activity_event(
        db,
        event_type=C.TRIMMER_TRIM_PLAN_CONSTRAINTS_CHECK_COMPLETED,
        module="trimmer",
        title="Trimmer trim plan constraint check",
        detail=detail,
    )
