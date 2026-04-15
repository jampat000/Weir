"""Sonarr-only failed import queue planning — consumes the shared eligibility decision.

No Radarr logic here. No HTTP or queue mutation: maps policy + Sonarr status text to a
plan a Sonarr client can execute (``DELETE /api/v3/queue/{id}`` with explicit flags).

The domain seam :func:`decide_failed_import_cleanup_eligibility` is always used with
``movies=False``; this module does not reclassify or reapply policy rules.
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
class SonarrFailedImportQueueDeletePlan:
    """Sonarr queue DELETE parameters derived from policy + classification."""

    decision: FailedImportCleanupEligibilityDecision
    remove_from_client: bool
    blocklist: bool
    sonarr_queue_item_id: int | None = None


def plan_sonarr_failed_import_cleanup(
    *,
    status_message_blob: str,
    policy: FailedImportCleanupPolicy,
    sonarr_queue_item_id: int | None = None,
) -> SonarrFailedImportQueueDeletePlan:
    """Build a queue DELETE plan from Sonarr-style status text and policy."""

    decision = decide_failed_import_cleanup_eligibility(status_message_blob, policy, movies=False)
    action = decision.configured_action
    if not decision.cleanup_eligible or action is None:
        return SonarrFailedImportQueueDeletePlan(
            decision=decision,
            remove_from_client=False,
            blocklist=False,
            sonarr_queue_item_id=sonarr_queue_item_id,
        )
    flags = queue_delete_flags_for_action(action)
    assert flags is not None
    rf, bl = flags
    return SonarrFailedImportQueueDeletePlan(
        decision=decision,
        remove_from_client=rf,
        blocklist=bl,
        sonarr_queue_item_id=sonarr_queue_item_id,
    )


def sonarr_plan_requests_queue_delete(plan: SonarrFailedImportQueueDeletePlan) -> bool:
    """True when this plan intends ``DELETE /api/v3/queue/{id}`` (any non-leave-alone handling)."""

    if not plan.decision.cleanup_eligible:
        return False
    return plan.decision.configured_action is not FailedImportQueueHandlingAction.LEAVE_ALONE
