"""Persist and list activity events — no writes from Activity API routes in this pass."""

from __future__ import annotations

from datetime import datetime, timedelta, timezone

from sqlalchemy import delete, desc, func, select
from sqlalchemy.orm import Session

from mediamop.platform.activity import constants as C
from mediamop.platform.activity.models import ActivityEvent
from mediamop.platform.suite_settings.service import ensure_suite_settings_row

RECENT_DEFAULT_LIMIT = 50

_LOGIN_FAILED_SUPPRESS_MINUTES = 2
_BOOTSTRAP_DENIED_SUPPRESS_SECONDS = 60


def _utcnow() -> datetime:
    return datetime.now(timezone.utc)


def record_activity_event(
    db: Session,
    *,
    event_type: str,
    module: str,
    title: str,
    detail: str | None = None,
) -> ActivityEvent:
    suite = ensure_suite_settings_row(db)
    _maybe_prune_activity_rows_by_retention(db, keep_days=max(1, min(int(suite.log_retention_days), 3650)))
    row = ActivityEvent(
        event_type=event_type,
        module=module,
        title=title,
        detail=detail,
    )
    db.add(row)
    db.flush()
    return row


def _maybe_prune_activity_rows_by_retention(db: Session, *, keep_days: int) -> None:
    cutoff = _utcnow() - timedelta(days=keep_days)
    db.execute(delete(ActivityEvent).where(ActivityEvent.created_at < cutoff))


def maybe_record_login_failed(db: Session, *, username: str) -> None:
    """One failed-login event per username per short window to limit brute-force noise."""

    cutoff = _utcnow() - timedelta(minutes=_LOGIN_FAILED_SUPPRESS_MINUTES)
    exists = db.scalars(
        select(ActivityEvent)
        .where(
            ActivityEvent.event_type == C.AUTH_LOGIN_FAILED,
            ActivityEvent.detail == username,
            ActivityEvent.created_at >= cutoff,
        )
        .limit(1),
    ).first()
    if exists is not None:
        return
    record_activity_event(
        db,
        event_type=C.AUTH_LOGIN_FAILED,
        module="auth",
        title="Sign-in failed",
        detail=username,
    )


def maybe_record_bootstrap_denied(db: Session) -> None:
    """At most one bootstrap-denied row per minute (clustered abuse)."""

    cutoff = _utcnow() - timedelta(seconds=_BOOTSTRAP_DENIED_SUPPRESS_SECONDS)
    exists = db.scalars(
        select(ActivityEvent)
        .where(
            ActivityEvent.event_type == C.AUTH_BOOTSTRAP_DENIED,
            ActivityEvent.created_at >= cutoff,
        )
        .limit(1),
    ).first()
    if exists is not None:
        return
    record_activity_event(
        db,
        event_type=C.AUTH_BOOTSTRAP_DENIED,
        module="auth",
        title="Bootstrap not allowed",
        detail="An admin account already exists.",
    )


def count_activity_events_since(db: Session, *, since: datetime) -> int:
    n = db.scalar(select(func.count()).select_from(ActivityEvent).where(ActivityEvent.created_at >= since))
    return int(n or 0)


def get_latest_activity_event(db: Session) -> ActivityEvent | None:
    return db.scalars(select(ActivityEvent).order_by(desc(ActivityEvent.created_at)).limit(1)).first()


def get_latest_activity_event_id(db: Session) -> int | None:
    """Cheap freshness probe for SSE invalidation: max persisted activity id."""

    v = db.scalar(select(func.max(ActivityEvent.id)))
    return int(v) if v is not None else None


def list_recent_activity_events(db: Session, *, limit: int = RECENT_DEFAULT_LIMIT) -> list[ActivityEvent]:
    lim = max(1, min(limit, 100))
    stmt = select(ActivityEvent).order_by(desc(ActivityEvent.created_at)).limit(lim)
    return list(db.scalars(stmt).all())
