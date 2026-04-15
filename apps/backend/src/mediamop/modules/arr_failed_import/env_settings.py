"""Failed-import queue handling loaded from the process environment.

Radarr and Sonarr each have their own handling set so defaults and persistence can diverge.

Env names use ``MEDIAMOP_FAILED_IMPORT_*`` only (see ``.env.example``).

Legacy boolean env keys (``MEDIAMOP_FAILED_IMPORT_*_CLEANUP_*``) still seed **remove_only** vs
**leave_alone** when the new per-class action env vars are unset.
"""

from __future__ import annotations

import os
from dataclasses import dataclass

from mediamop.modules.arr_failed_import.policy import FailedImportCleanupPolicy
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction


def _env_cleanup_bool(key: str) -> bool:
    raw = (os.environ.get(key) or "").strip().lower()
    if raw in ("1", "true", "yes", "on"):
        return True
    if raw in ("0", "false", "no", "off"):
        return False
    return False


def _parse_action_env(key: str, *, legacy_on: bool) -> FailedImportQueueHandlingAction:
    raw = (os.environ.get(key) or "").strip().lower().replace("-", "_")
    if not raw:
        return FailedImportQueueHandlingAction.REMOVE_ONLY if legacy_on else FailedImportQueueHandlingAction.LEAVE_ALONE
    try:
        return FailedImportQueueHandlingAction(raw)
    except ValueError:
        return FailedImportQueueHandlingAction.LEAVE_ALONE


@dataclass(frozen=True, slots=True)
class AppFailedImportCleanupPolicySettings:
    """Per-class Sonarr/Radarr queue handling for one *arr app."""

    handling_quality_rejection: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_unmatched_manual_import: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_sample_release: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_corrupt_import: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_failed_download: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_failed_import: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE

    def to_failed_import_cleanup_policy(self) -> FailedImportCleanupPolicy:
        return FailedImportCleanupPolicy(
            handling_quality_rejection=self.handling_quality_rejection,
            handling_unmatched_manual_import=self.handling_unmatched_manual_import,
            handling_sample_release=self.handling_sample_release,
            handling_corrupt_import=self.handling_corrupt_import,
            handling_failed_download=self.handling_failed_download,
            handling_failed_import=self.handling_failed_import,
        )


@dataclass(frozen=True, slots=True)
class FailedImportCleanupSettingsBundle:
    """App-separated cleanup policy settings; resolve with :meth:`radarr_policy` / :meth:`sonarr_policy`."""

    radarr: AppFailedImportCleanupPolicySettings
    sonarr: AppFailedImportCleanupPolicySettings

    def radarr_policy(self) -> FailedImportCleanupPolicy:
        return self.radarr.to_failed_import_cleanup_policy()

    def sonarr_policy(self) -> FailedImportCleanupPolicy:
        return self.sonarr.to_failed_import_cleanup_policy()


def _load_radarr_cleanup_settings() -> AppFailedImportCleanupPolicySettings:
    p = "MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_"
    return AppFailedImportCleanupPolicySettings(
        handling_quality_rejection=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_QUALITY",
            legacy_on=_env_cleanup_bool(p + "QUALITY"),
        ),
        handling_unmatched_manual_import=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_UNMATCHED",
            legacy_on=_env_cleanup_bool(p + "UNMATCHED"),
        ),
        handling_sample_release=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_SAMPLE",
            legacy_on=False,
        ),
        handling_corrupt_import=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_CORRUPT",
            legacy_on=_env_cleanup_bool(p + "CORRUPT"),
        ),
        handling_failed_download=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_DOWNLOAD_FAILED",
            legacy_on=_env_cleanup_bool(p + "DOWNLOAD_FAILED"),
        ),
        handling_failed_import=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_IMPORT_FAILED",
            legacy_on=_env_cleanup_bool(p + "IMPORT_FAILED"),
        ),
    )


def _load_sonarr_cleanup_settings() -> AppFailedImportCleanupPolicySettings:
    p = "MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_"
    return AppFailedImportCleanupPolicySettings(
        handling_quality_rejection=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_QUALITY",
            legacy_on=_env_cleanup_bool(p + "QUALITY"),
        ),
        handling_unmatched_manual_import=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_UNMATCHED",
            legacy_on=_env_cleanup_bool(p + "UNMATCHED"),
        ),
        handling_sample_release=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_SAMPLE",
            legacy_on=False,
        ),
        handling_corrupt_import=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_CORRUPT",
            legacy_on=_env_cleanup_bool(p + "CORRUPT"),
        ),
        handling_failed_download=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_DOWNLOAD_FAILED",
            legacy_on=_env_cleanup_bool(p + "DOWNLOAD_FAILED"),
        ),
        handling_failed_import=_parse_action_env(
            "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_IMPORT_FAILED",
            legacy_on=_env_cleanup_bool(p + "IMPORT_FAILED"),
        ),
    )


def default_failed_import_cleanup_settings_bundle() -> FailedImportCleanupSettingsBundle:
    """All classes ``leave_alone`` (no env read) — tests and manual ``MediaMopSettings`` construction."""

    off = AppFailedImportCleanupPolicySettings()
    return FailedImportCleanupSettingsBundle(radarr=off, sonarr=off)


def load_failed_import_cleanup_settings_bundle() -> FailedImportCleanupSettingsBundle:
    """Read cleanup handling from the process environment (default all ``leave_alone``)."""

    return FailedImportCleanupSettingsBundle(
        radarr=_load_radarr_cleanup_settings(),
        sonarr=_load_sonarr_cleanup_settings(),
    )
