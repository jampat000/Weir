"""Shared *arr download-queue failed-import rules (classification, policy, env toggles).

Neutral between Refiner and other modules so imports stay acyclic. Refiner and related services
consume these rules via narrow ports.
"""

from __future__ import annotations

from mediamop.modules.arr_failed_import.classification import (
    FailedImportOutcome,
    classify_failed_import_message,
    classify_failed_import_message_for_media,
    normalize_failed_import_blob,
)
from mediamop.modules.arr_failed_import.decision import (
    FailedImportCleanupEligibilityDecision,
    FailedImportCleanupEligibilityReason,
    decide_failed_import_cleanup_eligibility,
)
from mediamop.modules.arr_failed_import.env_settings import (
    AppFailedImportCleanupPolicySettings,
    FailedImportCleanupSettingsBundle,
    default_failed_import_cleanup_settings_bundle,
    load_failed_import_cleanup_settings_bundle,
)
from mediamop.modules.arr_failed_import.policy import (
    FailedImportCleanupPolicy,
    FailedImportCleanupPolicyKey,
    cleanup_policy_key_for_outcome,
    configured_action_for_terminal_outcome,
    default_failed_import_cleanup_policy,
    is_queue_delete_configured_for_outcome,
)
from mediamop.modules.arr_failed_import.queue_action import (
    FailedImportQueueHandlingAction,
    queue_delete_flags_for_action,
)

__all__ = [
    "AppFailedImportCleanupPolicySettings",
    "FailedImportCleanupEligibilityDecision",
    "FailedImportCleanupEligibilityReason",
    "FailedImportCleanupPolicy",
    "FailedImportCleanupPolicyKey",
    "FailedImportCleanupSettingsBundle",
    "FailedImportOutcome",
    "FailedImportQueueHandlingAction",
    "classify_failed_import_message",
    "classify_failed_import_message_for_media",
    "cleanup_policy_key_for_outcome",
    "configured_action_for_terminal_outcome",
    "decide_failed_import_cleanup_eligibility",
    "default_failed_import_cleanup_policy",
    "default_failed_import_cleanup_settings_bundle",
    "is_queue_delete_configured_for_outcome",
    "load_failed_import_cleanup_settings_bundle",
    "normalize_failed_import_blob",
    "queue_delete_flags_for_action",
]
