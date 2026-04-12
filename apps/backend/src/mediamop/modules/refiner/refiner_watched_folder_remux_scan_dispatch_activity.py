"""Activity writes for ``refiner.watched_folder.remux_scan_dispatch.v1``."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.platform.activity import constants as C
from mediamop.platform.activity.service import record_activity_event


def record_refiner_watched_folder_remux_scan_dispatch_completed(db: Session, *, detail: str | None) -> None:
    record_activity_event(
        db,
        event_type=C.REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_COMPLETED,
        module="refiner",
        title="Refiner watched-folder remux scan",
        detail=detail,
    )
