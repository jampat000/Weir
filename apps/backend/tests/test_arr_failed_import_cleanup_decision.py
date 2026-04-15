"""Pure failed-import cleanup eligibility decision (no execution)."""

from __future__ import annotations

import pytest

from mediamop.modules.arr_failed_import.classification import FailedImportOutcome
from mediamop.modules.arr_failed_import.decision import (
    FailedImportCleanupEligibilityDecision,
    FailedImportCleanupEligibilityReason,
    decide_failed_import_cleanup_eligibility,
)
from mediamop.modules.arr_failed_import.policy import (
    FailedImportCleanupPolicy,
    FailedImportCleanupPolicyKey,
    default_failed_import_cleanup_policy,
)
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction


def _policy_with_only(key: FailedImportCleanupPolicyKey, action: FailedImportQueueHandlingAction) -> FailedImportCleanupPolicy:
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


_TERMINAL_BLOBS: tuple[tuple[str, FailedImportOutcome, FailedImportCleanupPolicyKey, bool], ...] = (
    (
        "Not an upgrade for existing movie file",
        FailedImportOutcome.QUALITY,
        FailedImportCleanupPolicyKey.HANDLING_QUALITY_REJECTION,
        True,
    ),
    (
        "Manual Import required",
        FailedImportOutcome.UNMATCHED,
        FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT,
        True,
    ),
    (
        "file is corrupt",
        FailedImportOutcome.CORRUPT,
        FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT,
        True,
    ),
    (
        "Download failed",
        FailedImportOutcome.DOWNLOAD_FAILED,
        FailedImportCleanupPolicyKey.HANDLING_FAILED_DOWNLOAD,
        True,
    ),
    (
        "Import failed",
        FailedImportOutcome.IMPORT_FAILED,
        FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT,
        True,
    ),
    (
        "Not an upgrade for existing episode file",
        FailedImportOutcome.QUALITY,
        FailedImportCleanupPolicyKey.HANDLING_QUALITY_REJECTION,
        False,
    ),
)


@pytest.mark.parametrize("blob,outcome,key,movies", _TERMINAL_BLOBS)
def test_terminal_outcome_not_cleanup_eligible_when_leave_alone(
    blob: str,
    outcome: FailedImportOutcome,
    key: FailedImportCleanupPolicyKey,
    movies: bool,
) -> None:
    policy = _policy_with_only(key, FailedImportQueueHandlingAction.LEAVE_ALONE)
    d = decide_failed_import_cleanup_eligibility(blob, policy, movies=movies)
    assert d.outcome == outcome
    assert d.policy_key == key
    assert d.cleanup_eligible is False
    assert d.reason == FailedImportCleanupEligibilityReason.INELIGIBLE_CONFIGURED_LEAVE_ALONE


@pytest.mark.parametrize("blob,outcome,key,movies", _TERMINAL_BLOBS)
def test_terminal_outcome_cleanup_eligible_when_remove_only(
    blob: str,
    outcome: FailedImportOutcome,
    key: FailedImportCleanupPolicyKey,
    movies: bool,
) -> None:
    policy = _policy_with_only(key, FailedImportQueueHandlingAction.REMOVE_ONLY)
    d = decide_failed_import_cleanup_eligibility(blob, policy, movies=movies)
    assert d.outcome == outcome
    assert d.policy_key == key
    assert d.cleanup_eligible is True
    assert d.reason == FailedImportCleanupEligibilityReason.ELIGIBLE


def test_pending_waiting_never_cleanup_eligible_even_if_all_actions() -> None:
    policy = FailedImportCleanupPolicy(
        handling_quality_rejection=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_unmatched_manual_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_sample_release=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_corrupt_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_failed_download=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
    )
    d = decide_failed_import_cleanup_eligibility("Downloaded - Waiting to Import", policy, movies=True)
    assert d.outcome == FailedImportOutcome.PENDING_WAITING
    assert d.policy_key is None
    assert d.cleanup_eligible is False
    assert d.reason == FailedImportCleanupEligibilityReason.INELIGIBLE_NO_CLEANUP_POLICY_KEY


def test_unknown_never_cleanup_eligible() -> None:
    policy = FailedImportCleanupPolicy(handling_corrupt_import=FailedImportQueueHandlingAction.REMOVE_ONLY)
    d = decide_failed_import_cleanup_eligibility("Unrecognized status text.", policy, movies=True)
    assert d.outcome == FailedImportOutcome.UNKNOWN
    assert d.policy_key is None
    assert d.cleanup_eligible is False
    assert d.reason == FailedImportCleanupEligibilityReason.INELIGIBLE_NO_CLEANUP_POLICY_KEY


def test_blob_with_waiting_and_terminal_classifies_terminal_then_policy_applies() -> None:
    blob = "Downloaded - Waiting to Import. Manual Import required."
    policy_off = default_failed_import_cleanup_policy()
    d_off = decide_failed_import_cleanup_eligibility(blob, policy_off, movies=True)
    assert d_off.outcome == FailedImportOutcome.UNMATCHED
    assert d_off.policy_key == FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT
    assert d_off.cleanup_eligible is False
    assert d_off.reason == FailedImportCleanupEligibilityReason.INELIGIBLE_CONFIGURED_LEAVE_ALONE

    policy_on = _policy_with_only(
        FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT,
        FailedImportQueueHandlingAction.REMOVE_ONLY,
    )
    d_on = decide_failed_import_cleanup_eligibility(blob, policy_on, movies=True)
    assert d_on.outcome == FailedImportOutcome.UNMATCHED
    assert d_on.cleanup_eligible is True
    assert d_on.reason == FailedImportCleanupEligibilityReason.ELIGIBLE


def test_decision_is_frozen_dataclass_for_stable_shape() -> None:
    d = decide_failed_import_cleanup_eligibility("Import failed", default_failed_import_cleanup_policy(), movies=True)
    assert isinstance(d, FailedImportCleanupEligibilityDecision)
