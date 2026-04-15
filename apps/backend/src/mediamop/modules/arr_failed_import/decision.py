"""Pure eligibility decision: failed-import message blob + cleanup policy → no side effects.

Composes :func:`~mediamop.modules.arr_failed_import.classification.classify_failed_import_message_for_media`
with policy actions. Does not delete, mutate queues, or call *arr APIs.
"""

from __future__ import annotations

from dataclasses import dataclass
from enum import Enum

from mediamop.modules.arr_failed_import.classification import (
    FailedImportOutcome,
    classify_failed_import_message_for_media,
)
from mediamop.modules.arr_failed_import.policy import (
    FailedImportCleanupPolicy,
    FailedImportCleanupPolicyKey,
    cleanup_policy_key_for_outcome,
    configured_action_for_terminal_outcome,
    is_queue_delete_configured_for_outcome,
)
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction


class FailedImportCleanupEligibilityReason(str, Enum):
    """Why ``cleanup_eligible`` is true or false — stable for logs/UI later."""

    ELIGIBLE = "eligible"
    INELIGIBLE_NO_CLEANUP_POLICY_KEY = "ineligible_no_cleanup_policy_key"
    INELIGIBLE_CONFIGURED_LEAVE_ALONE = "ineligible_configured_leave_alone"


@dataclass(frozen=True, slots=True)
class FailedImportCleanupEligibilityDecision:
    """Result of applying classification + policy to one message blob."""

    outcome: FailedImportOutcome
    policy_key: FailedImportCleanupPolicyKey | None
    configured_action: FailedImportQueueHandlingAction | None
    cleanup_eligible: bool
    reason: FailedImportCleanupEligibilityReason


def decide_failed_import_cleanup_eligibility(
    message_blob: str,
    policy: FailedImportCleanupPolicy,
    *,
    movies: bool,
) -> FailedImportCleanupEligibilityDecision:
    """Classify ``message_blob`` and decide whether a queue DELETE should run for this policy.

    ``cleanup_eligible`` is true only when the outcome maps to a policy slot **and** the
    configured action is not :attr:`~mediamop.modules.arr_failed_import.queue_action.FailedImportQueueHandlingAction.LEAVE_ALONE`.

    :attr:`FailedImportOutcome.PENDING_WAITING` and :attr:`FailedImportOutcome.UNKNOWN` never
    yield a policy key and are never eligible.
    """
    outcome = classify_failed_import_message_for_media(message_blob, movies=movies)
    policy_key = cleanup_policy_key_for_outcome(outcome)
    configured = configured_action_for_terminal_outcome(outcome, policy)
    cleanup_eligible = is_queue_delete_configured_for_outcome(outcome, policy)

    if policy_key is None:
        reason = FailedImportCleanupEligibilityReason.INELIGIBLE_NO_CLEANUP_POLICY_KEY
    elif cleanup_eligible:
        reason = FailedImportCleanupEligibilityReason.ELIGIBLE
    else:
        reason = FailedImportCleanupEligibilityReason.INELIGIBLE_CONFIGURED_LEAVE_ALONE

    return FailedImportCleanupEligibilityDecision(
        outcome=outcome,
        policy_key=policy_key,
        configured_action=configured,
        cleanup_eligible=cleanup_eligible,
        reason=reason,
    )
