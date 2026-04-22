"""Compose dashboard payload — read-only for persistence."""

from __future__ import annotations

from datetime import datetime, timedelta, timezone

from sqlalchemy.orm import Session

from mediamop import __version__
from mediamop.core.config import MediaMopSettings
from mediamop.modules.dashboard.schemas import ActivitySummaryOut, DashboardStatusOut, SystemStatusOut
from mediamop.platform.activity.schemas import ActivityEventItemOut
from mediamop.platform.activity import service as activity_service
from mediamop.platform.health.service import get_health


def _build_activity_summary(db: Session) -> ActivitySummaryOut:
    since = datetime.now(timezone.utc) - timedelta(hours=24)
    n = activity_service.count_activity_events_since(db, since=since)
    latest_row = activity_service.get_latest_activity_event(db)
    latest = ActivityEventItemOut.model_validate(latest_row) if latest_row else None
    return ActivitySummaryOut(events_last_24h=n, latest=latest)


def build_dashboard_status(db: Session, settings: MediaMopSettings) -> DashboardStatusOut:
    _ = settings
    health = get_health()
    return DashboardStatusOut(
        system=SystemStatusOut(
            api_version=__version__,
            environment=settings.env,
            healthy=health.status == "ok",
        ),
        activity_summary=_build_activity_summary(db),
    )
