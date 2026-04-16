"""HTTP: read-only ``pruner_jobs`` inspection (Pruner lane)."""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, HTTPException, Query
from starlette import status

from mediamop.api.deps import DbSessionDep
from mediamop.modules.pruner.pruner_jobs_inspection_service import (
    list_pruner_jobs_for_inspection,
    validate_pruner_inspection_statuses,
)
from mediamop.modules.pruner.pruner_schemas import PrunerJobsInspectionOut
from mediamop.platform.auth.deps_auth import UserPublicDep

router = APIRouter(tags=["pruner"])


@router.get("/pruner/jobs/inspection", response_model=PrunerJobsInspectionOut)
def get_pruner_jobs_inspection(
    _user: UserPublicDep,
    db: DbSessionDep,
    limit: Annotated[int, Query(ge=1, le=100)] = 50,
    statuses: Annotated[
        list[str] | None,
        Query(
            alias="status",
            description="Optional filter by persisted status (repeat param).",
        ),
    ] = None,
) -> PrunerJobsInspectionOut:
    if statuses:
        try:
            st = validate_pruner_inspection_statuses(tuple(statuses))
        except ValueError as exc:
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
                detail=str(exc),
            ) from exc
        return list_pruner_jobs_for_inspection(db, limit=limit, statuses=st)
    return list_pruner_jobs_for_inspection(db, limit=limit, statuses=None)
