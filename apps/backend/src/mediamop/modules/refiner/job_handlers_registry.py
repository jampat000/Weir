"""Production Refiner job handler registry — one callable per persisted job kind (*arr cleanup drives)."""

from __future__ import annotations

from collections.abc import Callable, Mapping

from mediamop.core.config import MediaMopSettings
from mediamop.modules.refiner.radarr_failed_import_cleanup_job import (
    REFINER_JOB_KIND_RADARR_FAILED_IMPORT_CLEANUP_DRIVE,
    make_radarr_failed_import_cleanup_drive_handler,
)
from mediamop.modules.refiner.sonarr_failed_import_cleanup_job import (
    REFINER_JOB_KIND_SONARR_FAILED_IMPORT_CLEANUP_DRIVE,
    make_sonarr_failed_import_cleanup_drive_handler,
)
from mediamop.modules.refiner.worker_loop import RefinerJobWorkContext


def build_production_refiner_job_handlers(
    settings: MediaMopSettings,
) -> Mapping[str, Callable[[RefinerJobWorkContext], None]]:
    """Handlers the asyncio Refiner workers use (Radarr and Sonarr live drives; separate modules)."""

    return {
        REFINER_JOB_KIND_RADARR_FAILED_IMPORT_CLEANUP_DRIVE: make_radarr_failed_import_cleanup_drive_handler(
            settings,
        ),
        REFINER_JOB_KIND_SONARR_FAILED_IMPORT_CLEANUP_DRIVE: make_sonarr_failed_import_cleanup_drive_handler(
            settings,
        ),
    }
