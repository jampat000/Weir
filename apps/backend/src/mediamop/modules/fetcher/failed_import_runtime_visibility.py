"""Read-only failed-import settings snapshot (workers from config; cleanup schedules from SQLite)."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.core.config import MediaMopSettings, clamp_failed_import_cleanup_drive_schedule_interval_seconds
from mediamop.modules.fetcher.cleanup_policy_service import load_fetcher_failed_import_cleanup_bundle
from mediamop.modules.fetcher.schemas_failed_import_runtime_visibility import FailedImportRuntimeVisibilityOut

_VISIBILITY_NOTE = (
    "Saved preferences when this loaded. Not a live status panel for your apps or for sweeps that may be running."
)


def failed_import_runtime_visibility_from_db(session: Session, settings: MediaMopSettings) -> FailedImportRuntimeVisibilityOut:
    """Map loaded settings + persisted cleanup policy row to a bounded DTO (no liveness)."""

    n = settings.fetcher_worker_count
    disabled = n == 0
    if disabled:
        summary = "Background automation is off — scheduled failed-import sweeps will not start by themselves."
    elif n == 1:
        summary = "One background automation slot is enabled (typical for a single-server setup)."
    else:
        summary = (
            f"Several background automation slots are enabled ({n}). On one database this is unusual — "
            "confirm it is what you want before relying on it."
        )

    schedule_seed = (
        settings.failed_import_radarr_cleanup_drive_schedule_enabled,
        clamp_failed_import_cleanup_drive_schedule_interval_seconds(
            settings.failed_import_radarr_cleanup_drive_schedule_interval_seconds,
        ),
        settings.failed_import_sonarr_cleanup_drive_schedule_enabled,
        clamp_failed_import_cleanup_drive_schedule_interval_seconds(
            settings.failed_import_sonarr_cleanup_drive_schedule_interval_seconds,
        ),
    )
    _, row = load_fetcher_failed_import_cleanup_bundle(
        session,
        settings.failed_import_cleanup_env,
        schedule_seed=schedule_seed,
    )

    return FailedImportRuntimeVisibilityOut(
        background_job_worker_count=n,
        in_process_workers_disabled=disabled,
        in_process_workers_enabled=not disabled,
        worker_mode_summary=summary,
        failed_import_radarr_cleanup_drive_schedule_enabled=row.radarr_cleanup_drive_schedule_enabled,
        failed_import_radarr_cleanup_drive_schedule_interval_seconds=row.radarr_cleanup_drive_schedule_interval_seconds,
        failed_import_sonarr_cleanup_drive_schedule_enabled=row.sonarr_cleanup_drive_schedule_enabled,
        failed_import_sonarr_cleanup_drive_schedule_interval_seconds=row.sonarr_cleanup_drive_schedule_interval_seconds,
        visibility_note=_VISIBILITY_NOTE,
    )
