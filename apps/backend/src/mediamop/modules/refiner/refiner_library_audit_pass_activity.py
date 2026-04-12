"""Activity writes for the Refiner library audit pass family."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.platform.activity import constants as C
from mediamop.platform.activity.service import record_activity_event


def record_refiner_library_audit_pass_completed(db: Session, *, detail: str | None) -> None:
    record_activity_event(
        db,
        event_type=C.REFINER_LIBRARY_AUDIT_PASS_COMPLETED,
        module="refiner",
        title="Refiner library audit pass",
        detail=detail,
    )
