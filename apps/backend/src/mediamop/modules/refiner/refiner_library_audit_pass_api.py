"""Refiner HTTP: manual enqueue for library audit pass (``refiner_jobs`` only)."""

from __future__ import annotations

from fastapi import APIRouter, HTTPException, Request
from starlette import status

from mediamop.api.deps import DbSessionDep, SettingsDep
from mediamop.modules.refiner.refiner_library_audit_pass_enqueue import (
    enqueue_refiner_library_audit_pass_job,
)
from mediamop.modules.refiner.schemas_library_audit_pass_manual import (
    RefinerLibraryAuditPassManualEnqueueIn,
    RefinerLibraryAuditPassManualEnqueueOut,
)
from mediamop.platform.auth.authorization import RequireOperatorDep
from mediamop.platform.auth.csrf import (
    require_session_secret,
    validate_browser_post_origin,
    verify_csrf_token,
)

router = APIRouter(tags=["refiner"])


@router.post(
    "/refiner/jobs/library-audit-pass/enqueue",
    response_model=RefinerLibraryAuditPassManualEnqueueOut,
)
def post_refiner_library_audit_pass_enqueue(
    body: RefinerLibraryAuditPassManualEnqueueIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> RefinerLibraryAuditPassManualEnqueueOut:
    """Refiner: enqueue the library audit pass durable job (``refiner_jobs`` only)."""

    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, body.csrf_token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )

    job = enqueue_refiner_library_audit_pass_job(db)
    db.commit()
    return RefinerLibraryAuditPassManualEnqueueOut(
        job_id=job.id,
        dedupe_key=job.dedupe_key,
        job_kind=job.job_kind,
    )
