"""HTTP for persisted ``fetcher_jobs`` (inspection + manual finalize recovery on the Fetcher lane)."""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, HTTPException, Query, Request
from starlette import status

from mediamop.api.deps import DbSessionDep, SettingsDep
from mediamop.modules.fetcher.fetcher_job_recover_activity import record_fetcher_job_handler_ok_finalize_recovered
from mediamop.modules.fetcher.fetcher_jobs_inspection_service import (
    DEFAULT_TERMINAL_STATUSES,
    list_fetcher_jobs_for_inspection,
    validate_inspection_statuses,
)
from mediamop.modules.fetcher.fetcher_jobs_model import FetcherJob, FetcherJobStatus
from mediamop.modules.fetcher.fetcher_jobs_ops import recover_handler_ok_finalize_failed_to_completed
from mediamop.modules.fetcher.schemas_fetcher_jobs_inspection import FetcherJobsInspectionOut
from mediamop.modules.fetcher.schemas_recover_finalize import RecoverFinalizeFailureIn, RecoverFinalizeFailureOut
from mediamop.platform.auth.authorization import RequireOperatorDep
from mediamop.platform.auth.csrf import (
    require_session_secret,
    validate_browser_post_origin,
    verify_csrf_token,
)
from mediamop.platform.auth.deps_auth import UserPublicDep

router = APIRouter(tags=["fetcher"])


@router.get("/fetcher/jobs/inspection", response_model=FetcherJobsInspectionOut)
def get_fetcher_jobs_inspection(
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
) -> FetcherJobsInspectionOut:
    """Fetcher: read-only persisted ``fetcher_jobs`` rows (all job kinds on the Fetcher lane)."""

    if statuses:
        try:
            st = validate_inspection_statuses(tuple(statuses))
        except ValueError as exc:
            raise HTTPException(
                status_code=status.HTTP_422_UNPROCESSABLE_CONTENT,
                detail=str(exc),
            ) from exc
        return list_fetcher_jobs_for_inspection(
            db,
            limit=limit,
            statuses=st,
            default_terminal_only=False,
        )
    return list_fetcher_jobs_for_inspection(
        db,
        limit=limit,
        statuses=DEFAULT_TERMINAL_STATUSES,
        default_terminal_only=True,
    )


@router.post(
    "/fetcher/jobs/{job_id}/recover-finalize-failure",
    response_model=RecoverFinalizeFailureOut,
)
def post_fetcher_jobs_recover_finalize_failure(
    job_id: int,
    body: RecoverFinalizeFailureIn,
    request: Request,
    user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> RecoverFinalizeFailureOut:
    """Fetcher: mark a ``fetcher_jobs`` row ``completed`` when it is stuck in ``handler_ok_finalize_failed``."""

    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, body.csrf_token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )

    label = f"username={user.username} role={user.role}"
    outcome = recover_handler_ok_finalize_failed_to_completed(
        db,
        job_id=job_id,
        recovered_by_label=label,
    )
    if outcome == "not_found":
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Fetcher job not found.")
    if outcome == "wrong_status":
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail="Job is not in handler_ok_finalize_failed state (manual finish-only recovery applies there).",
        )
    job_row = db.get(FetcherJob, job_id)
    if job_row is not None:
        record_fetcher_job_handler_ok_finalize_recovered(db, job_id=job_id, job_kind=job_row.job_kind)
    return RecoverFinalizeFailureOut(
        job_id=job_id,
        status=FetcherJobStatus.COMPLETED.value,
    )
