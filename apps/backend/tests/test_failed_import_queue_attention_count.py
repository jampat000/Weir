"""Unit tests for failed-import queue attention counting (classification + policy)."""

from __future__ import annotations

from mediamop.modules.arr_failed_import.policy import FailedImportCleanupPolicy
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction
from mediamop.modules.fetcher.failed_import_queue_attention_service import (
    count_classified_failed_import_queue_rows,
)


def test_count_empty_queue() -> None:
    policy = FailedImportCleanupPolicy(handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY)
    assert count_classified_failed_import_queue_rows((), row_to_blob=lambda r: "", policy=policy, movies=True) == 0


def test_counts_import_failed_blob_when_policy_acts() -> None:
    policy = FailedImportCleanupPolicy(handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY)
    rows = ({"id": 1},)
    assert (
        count_classified_failed_import_queue_rows(
            rows,
            row_to_blob=lambda _r: "Error: Could not import download — import failed",
            policy=policy,
            movies=True,
        )
        == 1
    )


def test_does_not_count_terminal_outcome_when_configured_leave_alone() -> None:
    """Attention counts only rows the operator asked Fetcher to act on (non-``leave_alone``)."""

    policy = FailedImportCleanupPolicy(handling_failed_import=FailedImportQueueHandlingAction.LEAVE_ALONE)
    rows = ({"id": 1},)
    assert (
        count_classified_failed_import_queue_rows(
            rows,
            row_to_blob=lambda _r: "Error: Could not import download — import failed",
            policy=policy,
            movies=True,
        )
        == 0
    )


def test_skips_blank_blob() -> None:
    policy = FailedImportCleanupPolicy(handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY)
    rows = ({"id": 1},)
    assert count_classified_failed_import_queue_rows(rows, row_to_blob=lambda _r: "   ", policy=policy, movies=True) == 0


def test_sonarr_episode_quality_counts_when_policy_acts() -> None:
    policy = FailedImportCleanupPolicy(handling_quality_rejection=FailedImportQueueHandlingAction.REMOVE_ONLY)
    rows = ({"id": 1},)
    assert (
        count_classified_failed_import_queue_rows(
            rows,
            row_to_blob=lambda _r: "Not an upgrade for existing episode file",
            policy=policy,
            movies=False,
        )
        == 1
    )


def test_pending_waiting_never_counts_even_when_other_classes_act() -> None:
    """PENDING_WAITING has no policy slot — must not appear in needs-attention totals."""

    policy = FailedImportCleanupPolicy(
        handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
        handling_corrupt_import=FailedImportQueueHandlingAction.REMOVE_AND_BLOCKLIST,
    )
    rows = ({"id": 1},)
    assert (
        count_classified_failed_import_queue_rows(
            rows,
            row_to_blob=lambda _r: "Downloaded - Waiting to import",
            policy=policy,
            movies=True,
        )
        == 0
    )


def test_unknown_never_counts_even_when_other_classes_act() -> None:
    """UNKNOWN (unmatched text) has no policy slot — must not appear in needs-attention totals."""

    policy = FailedImportCleanupPolicy(
        handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
    )
    rows = ({"id": 1},)
    assert (
        count_classified_failed_import_queue_rows(
            rows,
            row_to_blob=lambda _r: "Totally novel status string with no rule match",
            policy=policy,
            movies=True,
        )
        == 0
    )
