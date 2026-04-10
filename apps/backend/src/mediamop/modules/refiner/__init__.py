"""Refiner module — domain and future product surface.

Pass 1–2: ownership/blocking and anchors. Pass 3: *arr queue adapters. Pass 4–6: failed
import classification, cleanup policy, and pure cleanup eligibility decisions. No HTTP
clients or destructive execution here.
"""

from __future__ import annotations

from mediamop.modules.refiner.arr_queue_plumbing import normalize_storage_path
from mediamop.modules.refiner.failed_import_classification import (
    FailedImportOutcome,
    classify_failed_import_message,
    normalize_failed_import_blob,
)
from mediamop.modules.refiner.failed_import_cleanup_decision import (
    FailedImportCleanupEligibilityDecision,
    FailedImportCleanupEligibilityReason,
    decide_failed_import_cleanup_eligibility,
)
from mediamop.modules.refiner.failed_import_cleanup_policy import (
    FailedImportCleanupPolicy,
    FailedImportCleanupPolicyKey,
    cleanup_policy_key_for_outcome,
    default_failed_import_cleanup_policy,
    is_failed_import_cleanup_enabled,
)
from mediamop.modules.refiner.domain import (
    FileAnchorCandidate,
    RefinerQueueRowView,
    TitleYearAnchor,
    extract_title_tokens_and_year,
    extract_title_year_anchor,
    file_is_owned_by_queue,
    normalize_titleish,
    row_owns_by_title_year_anchor,
    should_block_for_upstream,
    strip_packaging_tokens,
    title_year_anchors_match,
    tokenize_normalized,
)
from mediamop.modules.refiner.radarr_queue_adapter import map_radarr_queue_row_to_refiner_view
from mediamop.modules.refiner.sonarr_queue_adapter import map_sonarr_queue_row_to_refiner_view

__all__ = [
    "FailedImportCleanupEligibilityDecision",
    "FailedImportCleanupEligibilityReason",
    "decide_failed_import_cleanup_eligibility",
    "FailedImportCleanupPolicy",
    "FailedImportCleanupPolicyKey",
    "cleanup_policy_key_for_outcome",
    "default_failed_import_cleanup_policy",
    "is_failed_import_cleanup_enabled",
    "FailedImportOutcome",
    "classify_failed_import_message",
    "normalize_failed_import_blob",
    "FileAnchorCandidate",
    "map_radarr_queue_row_to_refiner_view",
    "map_sonarr_queue_row_to_refiner_view",
    "normalize_storage_path",
    "RefinerQueueRowView",
    "TitleYearAnchor",
    "extract_title_tokens_and_year",
    "extract_title_year_anchor",
    "file_is_owned_by_queue",
    "normalize_titleish",
    "row_owns_by_title_year_anchor",
    "should_block_for_upstream",
    "strip_packaging_tokens",
    "title_year_anchors_match",
    "tokenize_normalized",
]
