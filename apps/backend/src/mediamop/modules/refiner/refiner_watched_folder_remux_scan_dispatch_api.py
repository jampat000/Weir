"""Refiner HTTP: manual enqueue for watched-folder remux scan dispatch (``refiner_jobs`` only)."""

from __future__ import annotations

import json
from uuid import uuid4

from fastapi import APIRouter, HTTPException, Request
from starlette import status

from mediamop.api.deps import DbSessionDep, SettingsDep
from mediamop.modules.refiner.jobs_ops import refiner_enqueue_or_get_job
from mediamop.modules.refiner.refiner_path_settings_service import ensure_refiner_path_settings_row
from mediamop.modules.refiner.refiner_watched_folder_remux_scan_dispatch_job_kinds import (
    REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
)
from mediamop.modules.refiner.schemas_watched_folder_remux_scan_dispatch_manual import (
    RefinerWatchedFolderRemuxScanDispatchManualEnqueueIn,
    RefinerWatchedFolderRemuxScanDispatchManualEnqueueOut,
)
from mediamop.platform.auth.authorization import RequireOperatorDep
from mediamop.platform.auth.csrf import (
    require_session_secret,
    validate_browser_post_origin,
    verify_csrf_token,
)

router = APIRouter(tags=["refiner"])


@router.post(
    "/refiner/jobs/watched-folder-remux-scan-dispatch/enqueue",
    response_model=RefinerWatchedFolderRemuxScanDispatchManualEnqueueOut,
)
def post_refiner_watched_folder_remux_scan_dispatch_enqueue(
    body: RefinerWatchedFolderRemuxScanDispatchManualEnqueueIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> RefinerWatchedFolderRemuxScanDispatchManualEnqueueOut:
    """Enqueue one scan of the saved watched folder (manual trigger; Refiner-local)."""

    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, body.csrf_token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )

    row = ensure_refiner_path_settings_row(db)
    if not (row.refiner_watched_folder or "").strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=(
                "Refiner watched folder is not set in saved path settings. "
                "This scan reads media files under that folder — configure it first."
            ),
        )

    if body.enqueue_remux_jobs and not body.remux_dry_run and not (row.refiner_output_folder or "").strip():
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail=(
                "enqueue_remux_jobs with remux_dry_run false requires a saved Refiner output folder "
                "(live remux passes need it)."
            ),
        )

    payload = {
        "enqueue_remux_jobs": body.enqueue_remux_jobs,
        "remux_dry_run": body.remux_dry_run,
    }
    dedupe_key = f"{REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND}:{uuid4().hex}"
    job = refiner_enqueue_or_get_job(
        db,
        dedupe_key=dedupe_key,
        job_kind=REFINER_WATCHED_FOLDER_REMUX_SCAN_DISPATCH_JOB_KIND,
        payload_json=json.dumps(payload, separators=(",", ":")),
    )
    db.commit()
    return RefinerWatchedFolderRemuxScanDispatchManualEnqueueOut(
        job_id=job.id,
        dedupe_key=job.dedupe_key,
        job_kind=job.job_kind,
    )
