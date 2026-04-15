"""Live Sonarr/Radarr queue scan: count rows Fetcher would act on under the saved policy (read-only).

Contract: **needs attention** counts a queue row when its status text classifies to one of the
**six policy-backed terminal outcomes** (see ``cleanup_policy_key_for_outcome``) **and** the
operator set a handling action other than ``leave_alone`` for that class on that axis.
``PENDING_WAITING`` and ``UNKNOWN`` classifier results never produce a policy key and are never
counted, even if other classes are set to act. Rows configured ``leave_alone`` are not counted.
"""

from __future__ import annotations

from collections.abc import Callable, Mapping, Sequence
from datetime import datetime, timezone
from typing import Any

from sqlalchemy import select
from sqlalchemy.orm import Session

from mediamop.core.config import MediaMopSettings
from mediamop.modules.arr_failed_import.decision import decide_failed_import_cleanup_eligibility
from mediamop.modules.arr_failed_import.policy import FailedImportCleanupPolicy
from mediamop.modules.fetcher.cleanup_policy_service import load_fetcher_failed_import_cleanup_bundle
from mediamop.modules.fetcher.failed_import_drive_job_kinds import (
    FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
    FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE,
)
from mediamop.modules.fetcher.fetcher_arr_http_resolve import (
    resolve_radarr_http_credentials,
    resolve_sonarr_http_credentials,
)
from mediamop.modules.fetcher.fetcher_jobs_model import FetcherJob, FetcherJobStatus
from mediamop.modules.fetcher.radarr_failed_import_cleanup_drive import (
    RadarrQueueHttpFetchClient,
    radarr_queue_item_status_message_blob,
)
from mediamop.modules.fetcher.schemas_failed_import_queue_attention import (
    FetcherFailedImportQueueAttentionAxisOut,
    FetcherFailedImportQueueAttentionSnapshotOut,
)
from mediamop.modules.fetcher.sonarr_failed_import_cleanup_drive import (
    SonarrQueueHttpFetchClient,
    sonarr_queue_item_status_message_blob,
)


def count_classified_failed_import_queue_rows(
    rows: Sequence[Mapping[str, Any]],
    *,
    row_to_blob: Callable[[Mapping[str, Any]], str],
    policy: FailedImportCleanupPolicy,
    movies: bool,
) -> int:
    """Count queue rows that are terminal failed-import classes with a non-``leave_alone`` action."""

    n = 0
    for row in rows:
        blob = row_to_blob(row)
        if not blob.strip():
            continue
        decision = decide_failed_import_cleanup_eligibility(blob, policy, movies=movies)
        if decision.cleanup_eligible:
            n += 1
    return n


def _latest_completed_drive_job(session: Session, *, job_kind: str) -> FetcherJob | None:
    stmt = (
        select(FetcherJob)
        .where(
            FetcherJob.job_kind == job_kind,
            FetcherJob.status == FetcherJobStatus.COMPLETED.value,
        )
        .order_by(FetcherJob.updated_at.desc())
        .limit(1)
    )
    return session.scalars(stmt).first()


def _axis_out(
    session: Session,
    settings: MediaMopSettings,
    *,
    movies: bool,
    policy: FailedImportCleanupPolicy,
    job_kind: str,
    row_to_blob: Callable[[Mapping[str, Any]], str],
) -> FetcherFailedImportQueueAttentionAxisOut:
    job = _latest_completed_drive_job(session, job_kind=job_kind)
    fallback_last = job.updated_at if job is not None else None

    if movies:
        base, api_key = resolve_radarr_http_credentials(session, settings)
    else:
        base, api_key = resolve_sonarr_http_credentials(session, settings)

    if not base or not api_key:
        return FetcherFailedImportQueueAttentionAxisOut(
            needs_attention_count=None,
            last_checked_at=None,
        )

    try:
        if movies:
            rows = RadarrQueueHttpFetchClient(base, api_key).fetch_radarr_queue_items()
        else:
            rows = SonarrQueueHttpFetchClient(base, api_key).fetch_sonarr_queue_items()
        n = count_classified_failed_import_queue_rows(
            rows,
            row_to_blob=row_to_blob,
            policy=policy,
            movies=movies,
        )
        return FetcherFailedImportQueueAttentionAxisOut(
            needs_attention_count=n,
            last_checked_at=datetime.now(timezone.utc),
        )
    except Exception:
        return FetcherFailedImportQueueAttentionAxisOut(
            needs_attention_count=None,
            last_checked_at=fallback_last,
        )


def build_failed_import_queue_attention_snapshot(
    session: Session,
    settings: MediaMopSettings,
) -> FetcherFailedImportQueueAttentionSnapshotOut:
    bundle, _ = load_fetcher_failed_import_cleanup_bundle(session, settings.failed_import_cleanup_env)

    tv = _axis_out(
        session,
        settings,
        movies=False,
        policy=bundle.sonarr_policy(),
        job_kind=FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE,
        row_to_blob=sonarr_queue_item_status_message_blob,
    )
    movies = _axis_out(
        session,
        settings,
        movies=True,
        policy=bundle.radarr_policy(),
        job_kind=FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
        row_to_blob=radarr_queue_item_status_message_blob,
    )
    return FetcherFailedImportQueueAttentionSnapshotOut(tv_shows=tv, movies=movies)
