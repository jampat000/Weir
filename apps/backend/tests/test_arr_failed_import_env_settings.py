"""Failed import cleanup policy settings (env + MediaMopSettings seam)."""

from __future__ import annotations

from dataclasses import replace

import pytest

from mediamop.core.config import MediaMopSettings
from mediamop.modules.arr_failed_import.decision import decide_failed_import_cleanup_eligibility
from mediamop.modules.arr_failed_import.env_settings import (
    AppFailedImportCleanupPolicySettings,
    FailedImportCleanupSettingsBundle,
    default_failed_import_cleanup_settings_bundle,
    load_failed_import_cleanup_settings_bundle,
)
from mediamop.modules.arr_failed_import.policy import default_failed_import_cleanup_policy
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction

from tests.legacy_refiner_failed_import_env_poison import (
    LEGACY_REFINER_FAILED_IMPORT_CLEANUP_POLICY_ENV_KEYS,
    LEGACY_REFINER_RADARR_CLEANUP_CORRUPT,
    LEGACY_REFINER_SONARR_CLEANUP_IMPORT_FAILED,
)


def _clear_failed_import_cleanup_env(monkeypatch: pytest.MonkeyPatch) -> None:
    keys = (
        "MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_QUALITY",
        "MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_UNMATCHED",
        "MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_CORRUPT",
        "MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_DOWNLOAD_FAILED",
        "MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_IMPORT_FAILED",
        "MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_QUALITY",
        "MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_UNMATCHED",
        "MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_CORRUPT",
        "MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_DOWNLOAD_FAILED",
        "MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_IMPORT_FAILED",
        "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_QUALITY",
        "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_UNMATCHED",
        "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_SAMPLE",
        "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_CORRUPT",
        "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_DOWNLOAD_FAILED",
        "MEDIAMOP_FAILED_IMPORT_RADARR_ACTION_IMPORT_FAILED",
        "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_QUALITY",
        "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_UNMATCHED",
        "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_SAMPLE",
        "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_CORRUPT",
        "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_DOWNLOAD_FAILED",
        "MEDIAMOP_FAILED_IMPORT_SONARR_ACTION_IMPORT_FAILED",
        *LEGACY_REFINER_FAILED_IMPORT_CLEANUP_POLICY_ENV_KEYS,
    )
    for k in keys:
        monkeypatch.delenv(k, raising=False)


def test_default_bundle_resolves_to_domain_default_policy_all_off() -> None:
    b = default_failed_import_cleanup_settings_bundle()
    assert b.radarr.to_failed_import_cleanup_policy() == default_failed_import_cleanup_policy()
    assert b.sonarr.to_failed_import_cleanup_policy() == default_failed_import_cleanup_policy()


def test_load_bundle_reads_primary_failed_import_env(monkeypatch: pytest.MonkeyPatch) -> None:
    _clear_failed_import_cleanup_env(monkeypatch)
    monkeypatch.setenv("MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_CORRUPT", "1")
    monkeypatch.setenv("MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_IMPORT_FAILED", "true")
    b = load_failed_import_cleanup_settings_bundle()
    rp = b.radarr_policy()
    sp = b.sonarr_policy()
    assert rp.handling_corrupt_import is FailedImportQueueHandlingAction.REMOVE_ONLY
    assert rp.handling_failed_import is FailedImportQueueHandlingAction.LEAVE_ALONE
    assert sp.handling_failed_import is FailedImportQueueHandlingAction.REMOVE_ONLY
    assert sp.handling_corrupt_import is FailedImportQueueHandlingAction.LEAVE_ALONE


def test_legacy_refiner_cleanup_env_vars_are_ignored(monkeypatch: pytest.MonkeyPatch) -> None:
    _clear_failed_import_cleanup_env(monkeypatch)
    monkeypatch.setenv(LEGACY_REFINER_RADARR_CLEANUP_CORRUPT, "1")
    monkeypatch.setenv(LEGACY_REFINER_SONARR_CLEANUP_IMPORT_FAILED, "true")
    b = load_failed_import_cleanup_settings_bundle()
    assert b.radarr_policy().handling_corrupt_import is FailedImportQueueHandlingAction.LEAVE_ALONE
    assert b.sonarr_policy().handling_failed_import is FailedImportQueueHandlingAction.LEAVE_ALONE


def test_pending_unknown_remain_non_destructive_via_resolved_radarr_policy() -> None:
    b = default_failed_import_cleanup_settings_bundle()
    p = b.radarr_policy()
    assert (
        decide_failed_import_cleanup_eligibility("Downloaded - Waiting to Import", p, movies=True).cleanup_eligible
        is False
    )
    assert decide_failed_import_cleanup_eligibility("no known phrases", p, movies=True).cleanup_eligible is False


def test_failed_import_cleanup_policies_delegate_through_media_mop_settings() -> None:
    base = MediaMopSettings.load()
    bundle = FailedImportCleanupSettingsBundle(
        radarr=AppFailedImportCleanupPolicySettings(
            handling_quality_rejection=FailedImportQueueHandlingAction.REMOVE_ONLY,
        ),
        sonarr=AppFailedImportCleanupPolicySettings(
            handling_failed_download=FailedImportQueueHandlingAction.REMOVE_ONLY,
        ),
    )
    s = replace(base, failed_import_cleanup_env=bundle)
    assert s.radarr_failed_import_cleanup_policy().handling_quality_rejection is FailedImportQueueHandlingAction.REMOVE_ONLY
    assert s.radarr_failed_import_cleanup_policy().handling_failed_download is FailedImportQueueHandlingAction.LEAVE_ALONE
    assert s.sonarr_failed_import_cleanup_policy().handling_failed_download is FailedImportQueueHandlingAction.REMOVE_ONLY
    assert s.sonarr_failed_import_cleanup_policy().handling_quality_rejection is FailedImportQueueHandlingAction.LEAVE_ALONE


