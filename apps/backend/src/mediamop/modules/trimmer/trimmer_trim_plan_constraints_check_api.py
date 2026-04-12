"""Trimmer HTTP: manual enqueue for trim plan constraint check (``trimmer_jobs`` only)."""

from __future__ import annotations

import json
from uuid import uuid4

from fastapi import APIRouter, HTTPException, Request
from starlette import status

from mediamop.api.deps import DbSessionDep, SettingsDep
from mediamop.modules.trimmer.schemas_trim_plan_constraints_manual import (
    TrimmerTrimPlanConstraintsCheckManualEnqueueIn,
    TrimmerTrimPlanConstraintsCheckManualEnqueueOut,
)
from mediamop.modules.trimmer.trimmer_jobs_ops import trimmer_enqueue_or_get_job
from mediamop.modules.trimmer.trimmer_trim_plan_constraints_check_job_kinds import (
    TRIMMER_TRIM_PLAN_CONSTRAINTS_CHECK_JOB_KIND,
)
from mediamop.platform.auth.authorization import RequireOperatorDep
from mediamop.platform.auth.csrf import (
    require_session_secret,
    validate_browser_post_origin,
    verify_csrf_token,
)

router = APIRouter(tags=["trimmer"])


@router.post(
    "/trimmer/jobs/trim-plan-constraints-check/enqueue",
    response_model=TrimmerTrimPlanConstraintsCheckManualEnqueueOut,
)
def post_trimmer_trim_plan_constraints_check_enqueue(
    body: TrimmerTrimPlanConstraintsCheckManualEnqueueIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> TrimmerTrimPlanConstraintsCheckManualEnqueueOut:
    """Enqueue one payload-only evaluation of trim segment timing constraints."""

    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, body.csrf_token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )

    payload = {
        "segments": [{"start_sec": s.start_sec, "end_sec": s.end_sec} for s in body.segments],
        "source_duration_sec": body.source_duration_sec,
    }
    dedupe_key = f"{TRIMMER_TRIM_PLAN_CONSTRAINTS_CHECK_JOB_KIND}:{uuid4().hex}"
    job = trimmer_enqueue_or_get_job(
        db,
        dedupe_key=dedupe_key,
        job_kind=TRIMMER_TRIM_PLAN_CONSTRAINTS_CHECK_JOB_KIND,
        payload_json=json.dumps(payload, separators=(",", ":")),
    )
    db.commit()
    return TrimmerTrimPlanConstraintsCheckManualEnqueueOut(
        job_id=job.id,
        dedupe_key=job.dedupe_key,
        job_kind=job.job_kind,
    )
