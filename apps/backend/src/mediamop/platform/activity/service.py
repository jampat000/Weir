"""Persist and list activity events — no writes from Activity API in this pass."""

from __future__ import annotations

from sqlalchemy import desc, select
from sqlalchemy.orm import Session

from mediamop.platform.activity.models import ActivityEvent

RECENT_DEFAULT_LIMIT = 50


def record_activity_event(
    db: Session,
    *,
    event_type: str,
    module: str,
    title: str,
    detail: str | None = None,
) -> ActivityEvent:
    row = ActivityEvent(
        event_type=event_type,
        module=module,
        title=title,
        detail=detail,
    )
    db.add(row)
    db.flush()
    return row


def list_recent_activity_events(db: Session, *, limit: int = RECENT_DEFAULT_LIMIT) -> list[ActivityEvent]:
    lim = max(1, min(limit, 100))
    stmt = select(ActivityEvent).order_by(desc(ActivityEvent.created_at)).limit(lim)
    return list(db.scalars(stmt).all())
