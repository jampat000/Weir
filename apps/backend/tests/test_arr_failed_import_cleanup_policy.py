"""Policy key mapping and ``is_queue_delete_configured_for_outcome``."""

from __future__ import annotations

import pytest

from mediamop.modules.arr_failed_import.classification import FailedImportOutcome
from mediamop.modules.arr_failed_import.policy import (
    FailedImportCleanupPolicy,
    FailedImportCleanupPolicyKey,
    cleanup_policy_key_for_outcome,
    configured_action_for_terminal_outcome,
    default_failed_import_cleanup_policy,
    is_queue_delete_configured_for_outcome,
)
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction


def test_quality_maps_to_handling_quality_rejection_key() -> None:
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.QUALITY) == (
        FailedImportCleanupPolicyKey.HANDLING_QUALITY_REJECTION
    )


def test_unmatched_maps_to_handling_unmatched_manual_import_key() -> None:
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.UNMATCHED) == (
        FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT
    )


def test_sample_release_maps_to_handling_sample_release_key() -> None:
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.SAMPLE_RELEASE) == (
        FailedImportCleanupPolicyKey.HANDLING_SAMPLE_RELEASE
    )


def test_corrupt_maps_to_handling_corrupt_import_key() -> None:
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.CORRUPT) == (
        FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT
    )


def test_download_failed_maps_to_handling_failed_download_key() -> None:
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.DOWNLOAD_FAILED) == (
        FailedImportCleanupPolicyKey.HANDLING_FAILED_DOWNLOAD
    )


def test_import_failed_maps_to_handling_failed_import_key() -> None:
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.IMPORT_FAILED) == (
        FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT
    )


def test_pending_and_unknown_map_to_no_cleanup_policy_key() -> None:
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.PENDING_WAITING) is None
    assert cleanup_policy_key_for_outcome(FailedImportOutcome.UNKNOWN) is None


def test_is_queue_delete_configured_false_for_pending_and_unknown() -> None:
    p = default_failed_import_cleanup_policy()
    assert is_queue_delete_configured_for_outcome(FailedImportOutcome.PENDING_WAITING, p) is False
    assert is_queue_delete_configured_for_outcome(FailedImportOutcome.UNKNOWN, p) is False


@pytest.mark.parametrize(
    "outcome",
    [
        FailedImportOutcome.QUALITY,
        FailedImportOutcome.UNMATCHED,
        FailedImportOutcome.SAMPLE_RELEASE,
        FailedImportOutcome.CORRUPT,
        FailedImportOutcome.DOWNLOAD_FAILED,
        FailedImportOutcome.IMPORT_FAILED,
    ],
)
def test_is_queue_delete_configured_false_when_all_leave_alone(outcome: FailedImportOutcome) -> None:
    p = default_failed_import_cleanup_policy()
    assert is_queue_delete_configured_for_outcome(outcome, p) is False


def test_is_queue_delete_configured_true_when_corrupt_remove_only() -> None:
    p = FailedImportCleanupPolicy(handling_corrupt_import=FailedImportQueueHandlingAction.REMOVE_ONLY)
    assert is_queue_delete_configured_for_outcome(FailedImportOutcome.CORRUPT, p) is True
    assert is_queue_delete_configured_for_outcome(FailedImportOutcome.QUALITY, p) is False
    assert is_queue_delete_configured_for_outcome(FailedImportOutcome.UNMATCHED, p) is False


def test_configured_action_for_terminal_outcome_returns_none_for_pending() -> None:
    p = FailedImportCleanupPolicy(handling_failed_import=FailedImportQueueHandlingAction.BLOCKLIST_ONLY)
    assert configured_action_for_terminal_outcome(FailedImportOutcome.PENDING_WAITING, p) is None
