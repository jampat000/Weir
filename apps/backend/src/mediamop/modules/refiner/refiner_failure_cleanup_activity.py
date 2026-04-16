"""Activity writes for Refiner Pass 4 failure-cleanup sweeps."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.platform.activity import constants as C
from mediamop.platform.activity.service import record_activity_event


def record_refiner_failure_cleanup_sweep_completed(
    db: Session,
    *,
    media_scope: str,
    detail: str | None,
) -> None:
    label = "TV" if (media_scope or "").strip().lower() == "tv" else "Movies"
    record_activity_event(
        db,
        event_type=C.REFINER_FAILURE_CLEANUP_SWEEP_COMPLETED,
        module="refiner",
        title=f"Refiner failed remux cleanup sweep ({label})",
        detail=detail,
    )

