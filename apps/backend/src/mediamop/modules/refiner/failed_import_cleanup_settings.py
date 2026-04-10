"""Refiner failed import cleanup policy loaded from environment (Pass 9).

Radarr and Sonarr each have their own toggle set so defaults and future persistence
can diverge without a shared blob.

Env pattern (each defaults off when unset):

- ``MEDIAMOP_REFINER_RADARR_CLEANUP_QUALITY``
- ``MEDIAMOP_REFINER_RADARR_CLEANUP_UNMATCHED``
- ``MEDIAMOP_REFINER_RADARR_CLEANUP_CORRUPT``
- ``MEDIAMOP_REFINER_RADARR_CLEANUP_DOWNLOAD_FAILED``
- ``MEDIAMOP_REFINER_RADARR_CLEANUP_IMPORT_FAILED``

- ``MEDIAMOP_REFINER_SONARR_CLEANUP_QUALITY`` (and same suffixes)

Truthy: ``1``, ``true``, ``yes``, ``on`` (case-insensitive). Falsy: ``0``, ``false``,
``no``, ``off``, or empty/unset.
"""

from __future__ import annotations

import os
from dataclasses import dataclass

from mediamop.modules.refiner.failed_import_cleanup_policy import FailedImportCleanupPolicy

_RADARR_ENV_PREFIX = "MEDIAMOP_REFINER_RADARR_CLEANUP_"
_SONARR_ENV_PREFIX = "MEDIAMOP_REFINER_SONARR_CLEANUP_"


def _env_bool(name: str, *, default: bool = False) -> bool:
    raw = (os.environ.get(name) or "").strip().lower()
    if raw in ("1", "true", "yes", "on"):
        return True
    if raw in ("0", "false", "no", "off"):
        return False
    return default


@dataclass(frozen=True, slots=True)
class AppFailedImportCleanupPolicySettings:
    """Failed import cleanup toggles for one *arr app (Radarr or Sonarr)."""

    remove_quality_rejections: bool = False
    remove_unmatched_manual_import_rejections: bool = False
    remove_corrupt_imports: bool = False
    remove_failed_downloads: bool = False
    remove_failed_imports: bool = False

    def to_failed_import_cleanup_policy(self) -> FailedImportCleanupPolicy:
        return FailedImportCleanupPolicy(
            remove_quality_rejections=self.remove_quality_rejections,
            remove_unmatched_manual_import_rejections=self.remove_unmatched_manual_import_rejections,
            remove_corrupt_imports=self.remove_corrupt_imports,
            remove_failed_downloads=self.remove_failed_downloads,
            remove_failed_imports=self.remove_failed_imports,
        )


@dataclass(frozen=True, slots=True)
class RefinerFailedImportCleanupSettingsBundle:
    """App-separated cleanup policy settings; resolve with :meth:`radarr_policy` / :meth:`sonarr_policy`."""

    radarr: AppFailedImportCleanupPolicySettings
    sonarr: AppFailedImportCleanupPolicySettings

    def radarr_policy(self) -> FailedImportCleanupPolicy:
        return self.radarr.to_failed_import_cleanup_policy()

    def sonarr_policy(self) -> FailedImportCleanupPolicy:
        return self.sonarr.to_failed_import_cleanup_policy()


def _load_app_cleanup_settings(prefix: str) -> AppFailedImportCleanupPolicySettings:
    return AppFailedImportCleanupPolicySettings(
        remove_quality_rejections=_env_bool(prefix + "QUALITY"),
        remove_unmatched_manual_import_rejections=_env_bool(prefix + "UNMATCHED"),
        remove_corrupt_imports=_env_bool(prefix + "CORRUPT"),
        remove_failed_downloads=_env_bool(prefix + "DOWNLOAD_FAILED"),
        remove_failed_imports=_env_bool(prefix + "IMPORT_FAILED"),
    )


def default_refiner_failed_import_cleanup_settings_bundle() -> RefinerFailedImportCleanupSettingsBundle:
    """All toggles off (no env read) — tests and manual ``MediaMopSettings`` construction."""
    off = AppFailedImportCleanupPolicySettings()
    return RefinerFailedImportCleanupSettingsBundle(radarr=off, sonarr=off)


def load_refiner_failed_import_cleanup_settings_bundle() -> RefinerFailedImportCleanupSettingsBundle:
    """Read Refiner cleanup toggles from the process environment (default all off)."""
    return RefinerFailedImportCleanupSettingsBundle(
        radarr=_load_app_cleanup_settings(_RADARR_ENV_PREFIX),
        sonarr=_load_app_cleanup_settings(_SONARR_ENV_PREFIX),
    )
