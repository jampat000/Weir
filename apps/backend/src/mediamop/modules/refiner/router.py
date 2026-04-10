"""Authenticated read-only Refiner operational routes."""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, HTTPException, Query
from starlette import status

from mediamop.api.deps import DbSessionDep
from mediamop.modules.refiner.inspection_service import (
    DEFAULT_TERMINAL_STATUSES,
    list_refiner_jobs_for_inspection,
    validate_inspection_statuses,
)
from mediamop.modules.refiner.schemas_inspection import RefinerJobsInspectionOut
from mediamop.platform.auth.deps_auth import UserPublicDep

router = APIRouter(tags=["refiner"])


@router.get("/refiner/jobs/inspection", response_model=RefinerJobsInspectionOut)
def get_refiner_jobs_inspection(
    _user: UserPublicDep,
    db: DbSessionDep,
    limit: Annotated[int, Query(ge=1, le=100, description="Max rows to return.")] = 50,
    statuses: Annotated[
        list[str] | None,
        Query(
            alias="status",
            description=(
                "Filter by persisted status (repeat param). "
                "Omit to return only terminal rows: completed, failed, handler_ok_finalize_failed."
            ),
        ),
    ] = None,
) -> RefinerJobsInspectionOut:
    """Read-only job rows for operator inspection — ``handler_ok_finalize_failed`` is explicit, not folded into ``failed``."""

    if statuses:
        try:
            st = validate_inspection_statuses(tuple(statuses))
        except ValueError as exc:
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
                detail=str(exc),
            ) from exc
        return list_refiner_jobs_for_inspection(
            db,
            limit=limit,
            statuses=st,
            default_terminal_only=False,
        )
    return list_refiner_jobs_for_inspection(
        db,
        limit=limit,
        statuses=DEFAULT_TERMINAL_STATUSES,
        default_terminal_only=True,
    )
