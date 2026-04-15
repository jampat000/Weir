from __future__ import annotations

from fastapi import APIRouter

from mediamop.api.deps import DbSessionDep
from mediamop.modules.refiner.refiner_overview_stats_service import build_refiner_overview_stats
from mediamop.modules.refiner.schemas_refiner_overview_stats import RefinerOverviewStatsOut
from mediamop.platform.auth.authorization import RequireOperatorDep

router = APIRouter(tags=["refiner"])


@router.get("/refiner/overview-stats", response_model=RefinerOverviewStatsOut)
def get_refiner_overview_stats(
    db: DbSessionDep,
    _user: RequireOperatorDep,
) -> RefinerOverviewStatsOut:
    return build_refiner_overview_stats(db)
