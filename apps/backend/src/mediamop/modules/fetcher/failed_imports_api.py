"""Fetcher HTTP for Radarr/Sonarr download-queue failed-import workflow (policy, runtime, drives).

Manual ``handler_ok_finalize_failed`` → ``completed`` recovery for any ``fetcher_jobs`` row lives on
``fetcher_jobs_api``. Arr search manual enqueue lives in ``fetcher_arr_search_api``; persisted job inspection in
``fetcher_jobs_api``. Shared classification/policy for the download queue remains in ``mediamop.modules.arr_failed_import``.
"""

from __future__ import annotations

from enum import Enum

from fastapi import APIRouter, HTTPException, Request
from starlette import status
from sqlalchemy.orm import Session

from mediamop.api.deps import DbSessionDep, SettingsDep
from mediamop.core.config import MediaMopSettings, clamp_failed_import_cleanup_drive_schedule_interval_seconds
from mediamop.modules.fetcher.automation_summary_service import (
    build_fetcher_failed_import_automation_summary,
)
from mediamop.modules.fetcher.failed_import_queue_attention_service import (
    build_failed_import_queue_attention_snapshot,
)
from mediamop.modules.fetcher.cleanup_policy_model import FetcherFailedImportCleanupPolicyRow
from mediamop.modules.fetcher.cleanup_policy_service import (
    apply_fetcher_failed_import_cleanup_policy_axis_put,
    load_fetcher_failed_import_cleanup_bundle,
    upsert_fetcher_failed_import_cleanup_policy,
)
from mediamop.modules.fetcher.failed_import_activity import record_fetcher_failed_import_pass_queued
from mediamop.modules.fetcher.manual_cleanup_drive_enqueue import (
    manual_enqueue_radarr_cleanup_drive,
    manual_enqueue_sonarr_cleanup_drive,
)
from mediamop.modules.fetcher.schemas_automation_summary import FetcherFailedImportAutomationSummaryOut
from mediamop.modules.fetcher.schemas_cleanup_policy import (
    FailedImportCleanupPolicyAxisOut,
    FetcherFailedImportCleanupPolicyAxisPutIn,
    FetcherFailedImportCleanupPolicyOut,
    FetcherFailedImportCleanupPolicyPutIn,
)
from mediamop.modules.fetcher.schemas_manual_cleanup_enqueue import (
    ManualCleanupDriveEnqueueIn,
    ManualCleanupDriveEnqueueOut,
)
from mediamop.modules.fetcher.failed_import_runtime_visibility import (
    failed_import_runtime_visibility_from_db,
)
from mediamop.modules.fetcher.schemas_failed_import_queue_attention import (
    FetcherFailedImportQueueAttentionSnapshotOut,
)
from mediamop.modules.fetcher.schemas_failed_import_runtime_visibility import FailedImportRuntimeVisibilityOut
from mediamop.platform.auth.authorization import RequireOperatorDep
from mediamop.platform.auth.csrf import (
    require_session_secret,
    validate_browser_post_origin,
    verify_csrf_token,
)
from mediamop.platform.auth.deps_auth import UserPublicDep

router = APIRouter(tags=["fetcher"])


class FailedImportCleanupAxisPath(str, Enum):
    """URL segment for single-axis cleanup policy saves."""

    tv_shows = "tv-shows"
    movies = "movies"


def _axis_out_movies(row: FetcherFailedImportCleanupPolicyRow) -> FailedImportCleanupPolicyAxisOut:
    return FailedImportCleanupPolicyAxisOut(
        handling_quality_rejection=row.radarr_handling_quality_rejection,  # type: ignore[arg-type]
        handling_unmatched_manual_import=row.radarr_handling_unmatched_manual_import,  # type: ignore[arg-type]
        handling_sample_release=row.radarr_handling_sample_release,  # type: ignore[arg-type]
        handling_corrupt_import=row.radarr_handling_corrupt_import,  # type: ignore[arg-type]
        handling_failed_download=row.radarr_handling_failed_download,  # type: ignore[arg-type]
        handling_failed_import=row.radarr_handling_failed_import,  # type: ignore[arg-type]
        cleanup_drive_schedule_enabled=row.radarr_cleanup_drive_schedule_enabled,
        cleanup_drive_schedule_interval_seconds=row.radarr_cleanup_drive_schedule_interval_seconds,
    )


def _axis_out_tv_shows(row: FetcherFailedImportCleanupPolicyRow) -> FailedImportCleanupPolicyAxisOut:
    return FailedImportCleanupPolicyAxisOut(
        handling_quality_rejection=row.sonarr_handling_quality_rejection,  # type: ignore[arg-type]
        handling_unmatched_manual_import=row.sonarr_handling_unmatched_manual_import,  # type: ignore[arg-type]
        handling_sample_release=row.sonarr_handling_sample_release,  # type: ignore[arg-type]
        handling_corrupt_import=row.sonarr_handling_corrupt_import,  # type: ignore[arg-type]
        handling_failed_download=row.sonarr_handling_failed_download,  # type: ignore[arg-type]
        handling_failed_import=row.sonarr_handling_failed_import,  # type: ignore[arg-type]
        cleanup_drive_schedule_enabled=row.sonarr_cleanup_drive_schedule_enabled,
        cleanup_drive_schedule_interval_seconds=row.sonarr_cleanup_drive_schedule_interval_seconds,
    )


def _cleanup_policy_schedule_seed(settings: MediaMopSettings) -> tuple[bool, int, bool, int]:
    return (
        settings.failed_import_radarr_cleanup_drive_schedule_enabled,
        clamp_failed_import_cleanup_drive_schedule_interval_seconds(
            settings.failed_import_radarr_cleanup_drive_schedule_interval_seconds,
        ),
        settings.failed_import_sonarr_cleanup_drive_schedule_enabled,
        clamp_failed_import_cleanup_drive_schedule_interval_seconds(
            settings.failed_import_sonarr_cleanup_drive_schedule_interval_seconds,
        ),
    )


def _cleanup_policy_response(
    db: Session,
    settings: MediaMopSettings,
) -> FetcherFailedImportCleanupPolicyOut:
    _effective, row = load_fetcher_failed_import_cleanup_bundle(
        db,
        settings.failed_import_cleanup_env,
        schedule_seed=_cleanup_policy_schedule_seed(settings),
    )
    return FetcherFailedImportCleanupPolicyOut(
        movies=_axis_out_movies(row),
        tv_shows=_axis_out_tv_shows(row),
        updated_at=row.updated_at,
    )


@router.get(
    "/fetcher/failed-imports/cleanup-policy",
    response_model=FetcherFailedImportCleanupPolicyOut,
)
def get_fetcher_failed_imports_cleanup_policy(
    _user: UserPublicDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherFailedImportCleanupPolicyOut:
    """Fetcher: read effective removal rules for Radarr/Sonarr download-queue failed-import passes."""

    return _cleanup_policy_response(db, settings)


@router.put(
    "/fetcher/failed-imports/cleanup-policy",
    response_model=FetcherFailedImportCleanupPolicyOut,
)
def put_fetcher_failed_imports_cleanup_policy(
    body: FetcherFailedImportCleanupPolicyPutIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherFailedImportCleanupPolicyOut:
    """Fetcher: persist removal rules (movies and TV are independent)."""

    _verify_cleanup_put_csrf(request, settings, body.csrf_token)

    upsert_fetcher_failed_import_cleanup_policy(
        db,
        env_bundle=settings.failed_import_cleanup_env,
        radarr=body.movies.to_app_settings(),
        sonarr=body.tv_shows.to_app_settings(),
        radarr_cleanup_drive_schedule_enabled=body.movies.cleanup_drive_schedule_enabled,
        radarr_cleanup_drive_schedule_interval_seconds=body.movies.cleanup_drive_schedule_interval_seconds,
        sonarr_cleanup_drive_schedule_enabled=body.tv_shows.cleanup_drive_schedule_enabled,
        sonarr_cleanup_drive_schedule_interval_seconds=body.tv_shows.cleanup_drive_schedule_interval_seconds,
    )
    return _cleanup_policy_response(db, settings)


def _verify_cleanup_put_csrf(request: Request, settings: MediaMopSettings, token: str) -> None:
    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )


@router.put(
    "/fetcher/failed-imports/cleanup-policy/{axis}",
    response_model=FetcherFailedImportCleanupPolicyOut,
)
def put_fetcher_failed_imports_cleanup_policy_axis(
    axis: FailedImportCleanupAxisPath,
    body: FetcherFailedImportCleanupPolicyAxisPutIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherFailedImportCleanupPolicyOut:
    """Fetcher: persist Sonarr (TV) or Radarr (movies) cleanup rules only — the other app is unchanged."""

    _verify_cleanup_put_csrf(request, settings, body.csrf_token)
    internal = "tv_shows" if axis == FailedImportCleanupAxisPath.tv_shows else "movies"
    apply_fetcher_failed_import_cleanup_policy_axis_put(
        db,
        env_bundle=settings.failed_import_cleanup_env,
        axis=internal,
        policy=body.to_app_settings(),
        cleanup_drive_schedule_enabled=body.cleanup_drive_schedule_enabled,
        cleanup_drive_schedule_interval_seconds=body.cleanup_drive_schedule_interval_seconds,
    )
    return _cleanup_policy_response(db, settings)


@router.get(
    "/fetcher/failed-imports/automation-summary",
    response_model=FetcherFailedImportAutomationSummaryOut,
)
def get_fetcher_failed_imports_automation_summary(
    _user: UserPublicDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherFailedImportAutomationSummaryOut:
    """Fetcher: read-only last finished passes (per app) + saved schedule wording — persisted/settings only."""

    return build_fetcher_failed_import_automation_summary(db, settings)


@router.get(
    "/fetcher/failed-imports/queue-attention-snapshot",
    response_model=FetcherFailedImportQueueAttentionSnapshotOut,
)
def get_fetcher_failed_imports_queue_attention_snapshot(
    _user: UserPublicDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FetcherFailedImportQueueAttentionSnapshotOut:
    """Fetcher: live Sonarr/Radarr queue scan — counts rows that match a terminal failed-import class
    **and** have a non-``leave_alone`` handling action configured for that axis."""

    return build_failed_import_queue_attention_snapshot(db, settings)


@router.get("/fetcher/failed-imports/settings", response_model=FailedImportRuntimeVisibilityOut)
def get_fetcher_failed_imports_settings(
    _user: UserPublicDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> FailedImportRuntimeVisibilityOut:
    """Fetcher: read-only settings for in-process workers and Radarr/Sonarr timed failed-import passes.

    Does not report live worker health, pass execution, or app connectivity.
    """

    return failed_import_runtime_visibility_from_db(db, settings)


@router.post(
    "/fetcher/failed-imports/radarr/enqueue",
    response_model=ManualCleanupDriveEnqueueOut,
)
def post_fetcher_failed_imports_radarr_enqueue(
    body: ManualCleanupDriveEnqueueIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> ManualCleanupDriveEnqueueOut:
    """Fetcher: add a movies (Radarr) download-queue failed-import pass for the worker (deduped). Does not run the pass here."""

    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, body.csrf_token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )

    job, outcome = manual_enqueue_radarr_cleanup_drive(db)
    record_fetcher_failed_import_pass_queued(
        db,
        movies=True,
        source="manual",
        queue_outcome=outcome,
    )
    return ManualCleanupDriveEnqueueOut(
        job_id=job.id,
        dedupe_key=job.dedupe_key,
        job_kind=job.job_kind,
        queue_outcome=outcome,
    )


@router.post(
    "/fetcher/failed-imports/sonarr/enqueue",
    response_model=ManualCleanupDriveEnqueueOut,
)
def post_fetcher_failed_imports_sonarr_enqueue(
    body: ManualCleanupDriveEnqueueIn,
    request: Request,
    _user: RequireOperatorDep,
    db: DbSessionDep,
    settings: SettingsDep,
) -> ManualCleanupDriveEnqueueOut:
    """Fetcher: add a TV (Sonarr) download-queue failed-import pass for the worker (deduped). Does not run the pass here."""

    validate_browser_post_origin(request, settings)
    secret = require_session_secret(settings)
    if not verify_csrf_token(secret, body.csrf_token):
        raise HTTPException(
            status_code=status.HTTP_400_BAD_REQUEST,
            detail="Invalid or expired CSRF token.",
        )

    job, outcome = manual_enqueue_sonarr_cleanup_drive(db)
    record_fetcher_failed_import_pass_queued(
        db,
        movies=False,
        source="manual",
        queue_outcome=outcome,
    )
    return ManualCleanupDriveEnqueueOut(
        job_id=job.id,
        dedupe_key=job.dedupe_key,
        job_kind=job.job_kind,
        queue_outcome=outcome,
    )


