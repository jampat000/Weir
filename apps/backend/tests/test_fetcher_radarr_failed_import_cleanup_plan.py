"""Radarr failed import cleanup planning seam (Radarr-only, no executor)."""

from __future__ import annotations

from mediamop.modules.arr_failed_import.decision import decide_failed_import_cleanup_eligibility
from mediamop.modules.arr_failed_import.policy import (
    FailedImportCleanupPolicy,
    FailedImportCleanupPolicyKey,
    default_failed_import_cleanup_policy,
)
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction
from mediamop.modules.fetcher.radarr_failed_import_cleanup import (
    RadarrFailedImportQueueDeletePlan,
    plan_radarr_failed_import_cleanup,
    radarr_plan_requests_queue_delete,
)


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
    return FailedImportCleanupPolicy(handling_failed_import=action)


def test_radarr_terminal_failure_leave_alone_plans_no_delete() -> None:
    policy = _policy_only(FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT, FailedImportQueueHandlingAction.LEAVE_ALONE)
    plan = plan_radarr_failed_import_cleanup(
        status_message_blob="Import failed",
        policy=policy,
        radarr_queue_item_id=202,
    )
    assert radarr_plan_requests_queue_delete(plan) is False
    assert plan.decision.cleanup_eligible is False
    assert plan.radarr_queue_item_id == 202


def test_radarr_terminal_failure_remove_only_plans_queue_delete() -> None:
    policy = _policy_only(FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT, FailedImportQueueHandlingAction.REMOVE_ONLY)
    plan = plan_radarr_failed_import_cleanup(
        status_message_blob="Import failed",
        policy=policy,
    )
    assert radarr_plan_requests_queue_delete(plan) is True
    assert plan.remove_from_client is True and plan.blocklist is False
    assert plan.decision.cleanup_eligible is True


def test_radarr_pending_waiting_never_plans_delete() -> None:
    policy = FailedImportCleanupPolicy(
        handling_quality_rejection=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_unmatched_manual_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_sample_release=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_corrupt_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_failed_download=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
    )
    plan = plan_radarr_failed_import_cleanup(
        status_message_blob="Downloaded - Waiting to Import",
        policy=policy,
    )
    assert radarr_plan_requests_queue_delete(plan) is False
    assert plan.decision.cleanup_eligible is False


def test_radarr_unknown_never_plans_delete() -> None:
    plan = plan_radarr_failed_import_cleanup(
        status_message_blob="Something odd from Radarr.",
        policy=_policy_only(FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT, FailedImportQueueHandlingAction.REMOVE_ONLY),
    )
    assert radarr_plan_requests_queue_delete(plan) is False


def test_radarr_blob_waiting_plus_terminal_precedence_then_policy() -> None:
    blob = "Downloaded - Waiting to Import. Manual Import required."
    plan_off = plan_radarr_failed_import_cleanup(status_message_blob=blob, policy=default_failed_import_cleanup_policy())
    assert plan_off.decision.cleanup_eligible is False
    assert radarr_plan_requests_queue_delete(plan_off) is False

    plan_on = plan_radarr_failed_import_cleanup(
        status_message_blob=blob,
        policy=_policy_only(
            FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT,
            FailedImportQueueHandlingAction.REMOVE_ONLY,
        ),
    )
    assert plan_on.decision.cleanup_eligible is True
    assert radarr_plan_requests_queue_delete(plan_on) is True


def test_radarr_plan_decision_matches_pure_decision_seam() -> None:
    blob = "Not an upgrade for existing movie file"
    policy = _policy_only(FailedImportCleanupPolicyKey.HANDLING_QUALITY_REJECTION, FailedImportQueueHandlingAction.REMOVE_ONLY)
    expected = decide_failed_import_cleanup_eligibility(blob, policy, movies=True)
    plan = plan_radarr_failed_import_cleanup(status_message_blob=blob, policy=policy)
    assert plan.decision == expected


def test_radarr_failed_import_cleanup_types_are_radarr_named_not_shared_executor() -> None:
    assert "Radarr" in RadarrFailedImportQueueDeletePlan.__name__
