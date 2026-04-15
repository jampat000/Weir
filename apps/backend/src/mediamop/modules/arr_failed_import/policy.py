"""Per-class queue handling for policy-backed terminal failed-import outcomes.

Six :class:`~mediamop.modules.arr_failed_import.classification.FailedImportOutcome` values
(``QUALITY`` … ``IMPORT_FAILED``) map 1:1 to ``FailedImportCleanupPolicyKey`` / SQLite fields.

Classifier outcomes ``PENDING_WAITING`` and ``UNKNOWN`` exist on the same enum but **never**
select a policy key — they are not operator-configurable. See ADR-0010.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Final

from mediamop.modules.arr_failed_import.classification import FailedImportOutcome
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction


class FailedImportCleanupPolicyKey(str, Enum):
    """Stable keys for persisted / API handling fields — 1:1 with the six policy-backed terminal outcomes only."""

    HANDLING_QUALITY_REJECTION = "handling_quality_rejection"
    HANDLING_UNMATCHED_MANUAL_IMPORT = "handling_unmatched_manual_import"
    HANDLING_SAMPLE_RELEASE = "handling_sample_release"
    HANDLING_CORRUPT_IMPORT = "handling_corrupt_import"
    HANDLING_FAILED_DOWNLOAD = "handling_failed_download"
    HANDLING_FAILED_IMPORT = "handling_failed_import"


_TERMINAL_OUTCOME_TO_KEY: Final[dict[FailedImportOutcome, FailedImportCleanupPolicyKey]] = {
    FailedImportOutcome.QUALITY: FailedImportCleanupPolicyKey.HANDLING_QUALITY_REJECTION,
    FailedImportOutcome.UNMATCHED: FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT,
    FailedImportOutcome.SAMPLE_RELEASE: FailedImportCleanupPolicyKey.HANDLING_SAMPLE_RELEASE,
    FailedImportOutcome.CORRUPT: FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT,
    FailedImportOutcome.DOWNLOAD_FAILED: FailedImportCleanupPolicyKey.HANDLING_FAILED_DOWNLOAD,
    FailedImportOutcome.IMPORT_FAILED: FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT,
}


@dataclass(frozen=True, slots=True)
class FailedImportCleanupPolicy:
    """Per-outcome queue handling. Defaults keep every class in ``leave_alone``."""

    handling_quality_rejection: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_unmatched_manual_import: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_sample_release: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_corrupt_import: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_failed_download: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE
    handling_failed_import: FailedImportQueueHandlingAction = FailedImportQueueHandlingAction.LEAVE_ALONE

    def action_for_key(self, key: FailedImportCleanupPolicyKey) -> FailedImportQueueHandlingAction:
        if key is FailedImportCleanupPolicyKey.HANDLING_QUALITY_REJECTION:
            return self.handling_quality_rejection
        if key is FailedImportCleanupPolicyKey.HANDLING_UNMATCHED_MANUAL_IMPORT:
            return self.handling_unmatched_manual_import
        if key is FailedImportCleanupPolicyKey.HANDLING_SAMPLE_RELEASE:
            return self.handling_sample_release
        if key is FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT:
            return self.handling_corrupt_import
        if key is FailedImportCleanupPolicyKey.HANDLING_FAILED_DOWNLOAD:
            return self.handling_failed_download
        return self.handling_failed_import


def default_failed_import_cleanup_policy() -> FailedImportCleanupPolicy:
    """All classes default to ``leave_alone`` until explicitly changed."""

    return FailedImportCleanupPolicy()


def cleanup_policy_key_for_outcome(outcome: FailedImportOutcome) -> FailedImportCleanupPolicyKey | None:
    """Map a classifier outcome to its policy field key, or None if not a terminal cleanup case."""

    return _TERMINAL_OUTCOME_TO_KEY.get(outcome)


def configured_action_for_terminal_outcome(
    outcome: FailedImportOutcome,
    policy: FailedImportCleanupPolicy,
) -> FailedImportQueueHandlingAction | None:
    """Return the configured handling action for a terminal outcome, or None if not applicable."""

    key = cleanup_policy_key_for_outcome(outcome)
    if key is None:
        return None
    return policy.action_for_key(key)


def is_queue_delete_configured_for_outcome(outcome: FailedImportOutcome, policy: FailedImportCleanupPolicy) -> bool:
    """True when the operator chose a non-``leave_alone`` action for this terminal outcome."""

    action = configured_action_for_terminal_outcome(outcome, policy)
    if action is None:
        return False
    return action is not FailedImportQueueHandlingAction.LEAVE_ALONE
