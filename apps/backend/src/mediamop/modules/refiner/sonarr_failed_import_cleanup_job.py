"""Sonarr-only refiner_jobs producer + worker handler for the live failed-import cleanup drive.

Enqueue uses a stable dedupe key so only one sweep row exists at a time. The handler reuses
:func:`~mediamop.modules.refiner.sonarr_failed_import_cleanup_drive.drive_sonarr_failed_import_cleanup_from_live_queue`.
"""

from __future__ import annotations

from collections.abc import Callable

from sqlalchemy.orm import Session

from mediamop.core.config import MediaMopSettings
from mediamop.modules.refiner.jobs_model import RefinerJob
from mediamop.modules.refiner.jobs_ops import refiner_enqueue_or_get_job
from mediamop.modules.refiner.sonarr_cleanup_execution import SonarrQueueHttpClient
from mediamop.modules.refiner.sonarr_failed_import_cleanup_drive import (
    SonarrQueueHttpFetchClient,
    drive_sonarr_failed_import_cleanup_from_live_queue,
)
from mediamop.modules.refiner.worker_loop import RefinerJobWorkContext

REFINER_JOB_KIND_SONARR_FAILED_IMPORT_CLEANUP_DRIVE = "refiner.sonarr.failed_import_cleanup_drive.v1"

SONARR_FAILED_IMPORT_CLEANUP_DRIVE_DEDUPE_KEY = "refiner.sonarr.failed_import_cleanup_drive:v1"


def enqueue_sonarr_failed_import_cleanup_drive_job(session: Session) -> RefinerJob:
    """Insert or return the single durable Sonarr live cleanup sweep job."""

    return refiner_enqueue_or_get_job(
        session,
        dedupe_key=SONARR_FAILED_IMPORT_CLEANUP_DRIVE_DEDUPE_KEY,
        job_kind=REFINER_JOB_KIND_SONARR_FAILED_IMPORT_CLEANUP_DRIVE,
        payload_json="{}",
    )


def make_sonarr_failed_import_cleanup_drive_handler(
    settings: MediaMopSettings,
) -> Callable[[RefinerJobWorkContext], None]:
    """Build a worker handler that runs the existing Sonarr live drive (HTTP clients from settings)."""

    def _run(_ctx: RefinerJobWorkContext) -> None:
        base = settings.refiner_sonarr_base_url
        key = settings.refiner_sonarr_api_key
        if not base or not key:
            msg = (
                "Sonarr live cleanup drive requires MEDIAMOP_REFINER_SONARR_BASE_URL and "
                "MEDIAMOP_REFINER_SONARR_API_KEY"
            )
            raise RuntimeError(msg)

        fetch_client = SonarrQueueHttpFetchClient(base, key)
        queue_ops = SonarrQueueHttpClient(base, key)
        drive_sonarr_failed_import_cleanup_from_live_queue(
            settings,
            queue_fetch_client=fetch_client,
            queue_operations=queue_ops,
        )

    return _run
