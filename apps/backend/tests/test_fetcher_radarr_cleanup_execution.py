"""Radarr failed import cleanup execution (plan → client, Radarr-only)."""

from __future__ import annotations

from dataclasses import dataclass

from mediamop.modules.arr_failed_import.classification import FailedImportOutcome
from mediamop.modules.arr_failed_import.decision import (
    FailedImportCleanupEligibilityDecision,
    FailedImportCleanupEligibilityReason,
)
from mediamop.modules.arr_failed_import.policy import FailedImportCleanupPolicy, FailedImportCleanupPolicyKey
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction
from mediamop.modules.fetcher.radarr_cleanup_execution import (
    RadarrFailedImportCleanupExecutionOutcome,
    execute_radarr_failed_import_cleanup_plan,
)
from mediamop.modules.fetcher.radarr_failed_import_cleanup import (
    RadarrFailedImportQueueDeletePlan,
    plan_radarr_failed_import_cleanup,
)


@dataclass
class _RecordingRadarrClient:
    calls: list[tuple[int, bool, bool]]

    def __init__(self) -> None:
        self.calls = []

    def remove_queue_item(self, queue_item_id: int, *, remove_from_client: bool, blocklist: bool) -> None:
        self.calls.append((queue_item_id, remove_from_client, blocklist))


def test_none_plan_performs_no_client_action() -> None:
    d = FailedImportCleanupEligibilityDecision(
        outcome=FailedImportOutcome.PENDING_WAITING,
        policy_key=None,
        configured_action=None,
        cleanup_eligible=False,
        reason=FailedImportCleanupEligibilityReason.INELIGIBLE_NO_CLEANUP_POLICY_KEY,
    )
    plan = RadarrFailedImportQueueDeletePlan(
        decision=d,
        remove_from_client=False,
        blocklist=False,
        radarr_queue_item_id=42,
    )
    client = _RecordingRadarrClient()
    out = execute_radarr_failed_import_cleanup_plan(plan, client)
    assert out is RadarrFailedImportCleanupExecutionOutcome.NO_OP
    assert client.calls == []


def test_remove_only_with_queue_id_calls_client_once() -> None:
    d = FailedImportCleanupEligibilityDecision(
        outcome=FailedImportOutcome.IMPORT_FAILED,
        policy_key=FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT,
        configured_action=FailedImportQueueHandlingAction.REMOVE_ONLY,
        cleanup_eligible=True,
        reason=FailedImportCleanupEligibilityReason.ELIGIBLE,
    )
    plan = RadarrFailedImportQueueDeletePlan(
        decision=d,
        remove_from_client=True,
        blocklist=False,
        radarr_queue_item_id=77,
    )
    client = _RecordingRadarrClient()
    out = execute_radarr_failed_import_cleanup_plan(plan, client)
    assert out is RadarrFailedImportCleanupExecutionOutcome.REMOVED_REMOVE_ONLY
    assert client.calls == [(77, True, False)]


def test_blocklist_only_sets_flags() -> None:
    d = FailedImportCleanupEligibilityDecision(
        outcome=FailedImportOutcome.IMPORT_FAILED,
        policy_key=FailedImportCleanupPolicyKey.HANDLING_FAILED_IMPORT,
        configured_action=FailedImportQueueHandlingAction.BLOCKLIST_ONLY,
        cleanup_eligible=True,
        reason=FailedImportCleanupEligibilityReason.ELIGIBLE,
    )
    plan = RadarrFailedImportQueueDeletePlan(decision=d, remove_from_client=False, blocklist=True, radarr_queue_item_id=2)
    client = _RecordingRadarrClient()
    out = execute_radarr_failed_import_cleanup_plan(plan, client)
    assert out is RadarrFailedImportCleanupExecutionOutcome.REMOVED_BLOCKLIST_ONLY
    assert client.calls == [(2, False, True)]


def test_planned_remove_missing_queue_id_skips_client_safely() -> None:
    d = FailedImportCleanupEligibilityDecision(
        outcome=FailedImportOutcome.CORRUPT,
        policy_key=FailedImportCleanupPolicyKey.HANDLING_CORRUPT_IMPORT,
        configured_action=FailedImportQueueHandlingAction.REMOVE_ONLY,
        cleanup_eligible=True,
        reason=FailedImportCleanupEligibilityReason.ELIGIBLE,
    )
    plan = RadarrFailedImportQueueDeletePlan(
        decision=d,
        remove_from_client=True,
        blocklist=False,
        radarr_queue_item_id=None,
    )
    client = _RecordingRadarrClient()
    out = execute_radarr_failed_import_cleanup_plan(plan, client)
    assert out is RadarrFailedImportCleanupExecutionOutcome.SKIPPED_MISSING_QUEUE_ITEM_ID
    assert client.calls == []


def test_planner_produced_plan_round_trips_through_executor_without_replanning() -> None:
    plan = plan_radarr_failed_import_cleanup(
        status_message_blob="Import failed",
        policy=FailedImportCleanupPolicy(handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY),
        radarr_queue_item_id=888,
    )
    client = _RecordingRadarrClient()
    out = execute_radarr_failed_import_cleanup_plan(plan, client)
    assert out is RadarrFailedImportCleanupExecutionOutcome.REMOVED_REMOVE_ONLY
    assert client.calls == [(888, True, False)]
