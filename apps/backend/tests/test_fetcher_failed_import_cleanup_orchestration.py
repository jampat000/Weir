"""Failed import cleanup orchestration routing (Radarr vs Sonarr planners)."""

from __future__ import annotations

from mediamop.modules.arr_failed_import.policy import (
    FailedImportCleanupPolicy,
    FailedImportCleanupPolicyKey,
)
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction
from mediamop.modules.fetcher.failed_import_cleanup_orchestration import (
    FailedImportArrApp,
    plan_failed_import_cleanup,
)
from mediamop.modules.fetcher.radarr_failed_import_cleanup import RadarrFailedImportQueueDeletePlan
from mediamop.modules.fetcher.sonarr_failed_import_cleanup import SonarrFailedImportQueueDeletePlan


def _policy_only(key: FailedImportCleanupPolicyKey, action: FailedImportQueueHandlingAction) -> FailedImportCleanupPolicy:
    if key is FailedImportCleanupPolicyKey.HANDLING_QUALITY_REJECTION:
        return FailedImportCleanupPolicy(handling_quality_rejection=action)
    if key is FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT:
        return FailedImportCleanupPolicy(handling_unmatched_manual_import=action)
    if key is FailedImportCleanupPolicyKey.HANDLING_SAMPLE_RELEASE:
        return FailedImportCleanupPolicy(handling_sample_release=action)
    if key is FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT:
        return FailedImportCleanupPolicy(handling_corrupt_import=action)
    if key is FailedImportCleanupPolicyKey.HANDLING_FAILED_DOWNLOAD:
        return FailedImportCleanupPolicy(handling_failed_download=action)
    if key is FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT:
        return FailedImportCleanupPolicy(handling_failed_import=action)
    raise AssertionError(f"unhandled key {key!r}")


def test_plan_failed_import_cleanup_radarr_routes_to_radarr_planner() -> None:
    policy = _policy_only(FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT, FailedImportQueueHandlingAction.REMOVE_ONLY)
    via = plan_failed_import_cleanup(
        FailedImportArrApp.RADARR,
        status_message_blob="Import failed",
        policy=policy,
        queue_item_id=9,
    )
    assert isinstance(via, RadarrFailedImportQueueDeletePlan)
    assert via.radarr_queue_item_id == 9


def test_plan_failed_import_cleanup_sonarr_routes_to_sonarr_planner() -> None:
    policy = _policy_only(FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT, FailedImportQueueHandlingAction.REMOVE_ONLY)
    via = plan_failed_import_cleanup(
        FailedImportArrApp.SONARR,
        status_message_blob="file is corrupt",
        policy=policy,
        queue_item_id=10,
    )
    assert isinstance(via, SonarrFailedImportQueueDeletePlan)
    assert via.sonarr_queue_item_id == 10


def test_parse_failed_import_arr_app_accepts_case_insensitive() -> None:
    assert plan_failed_import_cleanup(
        FailedImportArrApp("radarr"),
        status_message_blob="Import failed",
        policy=_policy_only(FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT, FailedImportQueueHandlingAction.REMOVE_ONLY),
        queue_item_id=1,
    )
    r = plan_failed_import_cleanup(
        FailedImportArrApp.RADARR,
        status_message_blob="x",
        policy=FailedImportCleanupPolicy(),
        queue_item_id=None,
    )
    s = plan_failed_import_cleanup(
        FailedImportArrApp.SONARR,
        status_message_blob="x",
        policy=FailedImportCleanupPolicy(),
        queue_item_id=None,
    )
    assert isinstance(r, RadarrFailedImportQueueDeletePlan)
    assert isinstance(s, SonarrFailedImportQueueDeletePlan)
