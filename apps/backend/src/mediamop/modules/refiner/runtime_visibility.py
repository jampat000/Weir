"""Map :class:`~mediamop.core.config.MediaMopSettings` to a bounded Refiner runtime visibility DTO.

No asyncio introspection — **configured intent** only, not proved task liveness.
"""

from __future__ import annotations

from mediamop.core.config import MediaMopSettings
from mediamop.modules.refiner.schemas_runtime_visibility import RefinerRuntimeVisibilityOut

_VISIBILITY_NOTE = (
    "Taken from saved settings when this was loaded. This is not proof that background runners, "
    "timed download-queue passes, or connections to your movie and TV apps are running or reachable."
)


def refiner_runtime_visibility_from_settings(settings: MediaMopSettings) -> RefinerRuntimeVisibilityOut:
    """Map loaded settings to a bounded inspection DTO (Refiner-local; no DB)."""

    n = settings.refiner_worker_count
    disabled = n == 0
    if disabled:
        summary = (
            "Refiner background runners are off (configured count is 0). "
            "Queued tasks will not start automatically."
        )
    elif n == 1:
        summary = (
            "One background runner is enabled — the usual default for a single SQLite database."
        )
    else:
        summary = (
            f"Multiple background runners are enabled (count {n}). "
            "On SQLite this is a guarded setup and not the recommended default — "
            "confirm behavior before relying on it in production."
        )

    return RefinerRuntimeVisibilityOut(
        refiner_worker_count=n,
        in_process_workers_disabled=disabled,
        in_process_workers_enabled=not disabled,
        worker_mode_summary=summary,
        refiner_radarr_cleanup_drive_schedule_enabled=settings.refiner_radarr_cleanup_drive_schedule_enabled,
        refiner_radarr_cleanup_drive_schedule_interval_seconds=(
            settings.refiner_radarr_cleanup_drive_schedule_interval_seconds
        ),
        refiner_sonarr_cleanup_drive_schedule_enabled=settings.refiner_sonarr_cleanup_drive_schedule_enabled,
        refiner_sonarr_cleanup_drive_schedule_interval_seconds=(
            settings.refiner_sonarr_cleanup_drive_schedule_interval_seconds
        ),
        visibility_note=_VISIBILITY_NOTE,
    )
