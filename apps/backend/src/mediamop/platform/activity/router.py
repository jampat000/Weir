"""Read-only Activity feed API."""

from __future__ import annotations

from fastapi import APIRouter

from mediamop.api.deps import DbSessionDep
from mediamop.platform.activity.schemas import ActivityEventItemOut, ActivityRecentOut
from mediamop.platform.activity.service import list_recent_activity_events
from mediamop.platform.auth.deps_auth import UserPublicDep

router = APIRouter(prefix="/activity", tags=["activity"])


@router.get("/recent", response_model=ActivityRecentOut)
def get_activity_recent(
    _user: UserPublicDep,
    db: DbSessionDep,
) -> ActivityRecentOut:
    """Recent persisted events, newest first — snapshot only (no live transport in Stage 8 Pass 1)."""

    rows = list_recent_activity_events(db)
    return ActivityRecentOut(
        items=[ActivityEventItemOut.model_validate(r) for r in rows],
    )
