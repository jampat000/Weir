"""Radarr-only failed import queue planning — consumes the shared eligibility decision.

No Sonarr logic here. No HTTP or queue mutation: maps policy + Radarr status text to a
plan a Radarr client can execute (``DELETE /api/v3/queue/{id}`` with explicit flags).

The domain seam :func:`decide_failed_import_cleanup_eligibility` is always used with
``movies=True``; this module does not reclassify or reapply policy rules.
"""

from __future__ import annotations

from dataclasses import dataclass

from mediamop.modules.arr_failed_import.decision import (
    FailedImportCleanupEligibilityDecision,
    decide_failed_import_cleanup_eligibility,
)
from mediamop.modules.arr_failed_import.policy import FailedImportCleanupPolicy
from mediamop.modules.arr_failed_import.queue_action import (
    FailedImportQueueHandlingAction,
    queue_delete_flags_for_action,
)


@dataclass(frozen=True, slots=True)
class RadarrFailedImportQueueDeletePlan:
    """Radarr queue DELETE parameters derived from policy + classification."""

    decision: FailedImportCleanupEligibilityDecision
    remove_from_client: bool
    blocklist: bool
    radarr_queue_item_id: int | None = None


def plan_radarr_failed_import_cleanup(
    *,
    status_message_blob: str,
    policy: FailedImportCleanupPolicy,
    radarr_queue_item_id: int | None = None,
) -> RadarrFailedImportQueueDeletePlan:
    """Build a queue DELETE plan from Radarr-style status text and policy."""

    decision = decide_failed_import_cleanup_eligibility(status_message_blob, policy, movies=True)
    action = decision.configured_action
    if not decision.cleanup_eligible or action is None:
        return RadarrFailedImportQueueDeletePlan(
            decision=decision,
            remove_from_client=False,
            blocklist=False,
            radarr_queue_item_id=radarr_queue_item_id,
        )
    flags = queue_delete_flags_for_action(action)
    assert flags is not None
    rf, bl = flags
    return RadarrFailedImportQueueDeletePlan(
        decision=decision,
        remove_from_client=rf,
        blocklist=bl,
        radarr_queue_item_id=radarr_queue_item_id,
    )


def radarr_plan_requests_queue_delete(plan: RadarrFailedImportQueueDeletePlan) -> bool:
    """True when this plan intends ``DELETE /api/v3/queue/{id}`` (any non-leave-alone handling)."""

    if not plan.decision.cleanup_eligible:
        return False
    return plan.decision.configured_action is not FailedImportQueueHandlingAction.LEAVE_ALONE
