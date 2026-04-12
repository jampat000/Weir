"""Map :class:`~mediamop.core.config.MediaMopSettings` to a bounded Refiner-only runtime snapshot."""

from __future__ import annotations

from mediamop.core.config import MediaMopSettings
from mediamop.modules.refiner.schemas_refiner_runtime_visibility import RefinerRuntimeSettingsOut

_VISIBILITY_NOTE = (
    "Values reflect this API process startup configuration. They do not prove a worker is mid-job "
    "or that another process is not also using the same database file."
)

_SQLITE_THROUGHPUT_NOTE = (
    "MediaMop stores durable jobs on SQLite. Several Refiner workers can each claim a different "
    "refiner_jobs row, but the database still serializes writes, so raising the count may not "
    "speed things up proportionally and can add contention."
)

_CONFIGURATION_NOTE = (
    "Change MEDIAMOP_REFINER_WORKER_COUNT in apps/backend/.env (integer 0–8: 0 = off, 1 = one worker, "
    "2–8 = several concurrent workers for this lane only), then restart the MediaMop API."
)


def refiner_runtime_settings_from_settings(settings: MediaMopSettings) -> RefinerRuntimeSettingsOut:
    """Map loaded settings to a DTO (no DB reads; Refiner-owned semantics)."""

    n = settings.refiner_worker_count
    disabled = n == 0
    if disabled:
        summary = (
            "In-process Refiner workers are off (0). refiner_jobs rows stay queued until you set "
            "MEDIAMOP_REFINER_WORKER_COUNT to at least 1 and restart this API."
        )
    elif n == 1:
        summary = (
            "One in-process Refiner worker is configured — it processes refiner_jobs one lease at a time "
            "(the usual default when automation is on)."
        )
    else:
        summary = (
            f"{n} in-process Refiner workers are configured — each can lease a different refiner_jobs row. "
            "Only this lane’s worker count is controlled here; other module lanes use their own settings."
        )

    return RefinerRuntimeSettingsOut(
        in_process_refiner_worker_count=n,
        in_process_workers_disabled=disabled,
        in_process_workers_enabled=not disabled,
        worker_mode_summary=summary,
        sqlite_throughput_note=_SQLITE_THROUGHPUT_NOTE,
        configuration_note=_CONFIGURATION_NOTE,
        visibility_note=_VISIBILITY_NOTE,
    )
