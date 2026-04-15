"""Sonarr-only failed import queue execution — consumes :class:`SonarrFailedImportQueueDeletePlan`.

Does not reclassify or re-evaluate policy; only interprets the plan and ``sonarr_queue_item_id``.
Inject a :class:`SonarrQueueOperations` implementation (HTTP or mock) at the boundary.

HTTP contract: ``DELETE {base}/api/v3/queue/{id}`` with explicit ``removeFromClient`` and
``blocklist`` query params — see Sonarr ``Api.V3/Queue/QueueController`` (ADR-0011).
"""

from __future__ import annotations

import urllib.error
import urllib.parse
import urllib.request
from enum import Enum
from typing import Protocol

from mediamop.modules.fetcher.sonarr_failed_import_cleanup import (
    SonarrFailedImportQueueDeletePlan,
    sonarr_plan_requests_queue_delete,
)


class SonarrQueueOperations(Protocol):
    """Narrow Sonarr queue API surface for cleanup execution (easy to fake in tests)."""

    def remove_queue_item(
        self,
        queue_item_id: int,
        *,
        remove_from_client: bool,
        blocklist: bool,
    ) -> None:
        """Delete one queue row (Sonarr v3 ``DELETE /api/v3/queue/{id}`` with explicit flags)."""
        ...


class SonarrFailedImportCleanupExecutionOutcome(str, Enum):
    """Outcome of applying a Sonarr failed-import queue plan via a queue client."""

    NO_OP = "no_op"
    REMOVED_REMOVE_ONLY = "removed_remove_only"
    REMOVED_BLOCKLIST_ONLY = "removed_blocklist_only"
    REMOVED_REMOVE_AND_BLOCKLIST = "removed_remove_and_blocklist"
    SKIPPED_MISSING_QUEUE_ITEM_ID = "skipped_missing_queue_item_id"


def execute_sonarr_failed_import_cleanup_plan(
    plan: SonarrFailedImportQueueDeletePlan,
    queue_client: SonarrQueueOperations,
) -> SonarrFailedImportCleanupExecutionOutcome:
    """Apply ``plan`` using ``queue_client`` when a queue DELETE is intended and an id exists."""

    if not sonarr_plan_requests_queue_delete(plan):
        return SonarrFailedImportCleanupExecutionOutcome.NO_OP
    if plan.sonarr_queue_item_id is None:
        return SonarrFailedImportCleanupExecutionOutcome.SKIPPED_MISSING_QUEUE_ITEM_ID
    queue_client.remove_queue_item(
        plan.sonarr_queue_item_id,
        remove_from_client=plan.remove_from_client,
        blocklist=plan.blocklist,
    )
    if plan.remove_from_client and plan.blocklist:
        return SonarrFailedImportCleanupExecutionOutcome.REMOVED_REMOVE_AND_BLOCKLIST
    if plan.blocklist:
        return SonarrFailedImportCleanupExecutionOutcome.REMOVED_BLOCKLIST_ONLY
    return SonarrFailedImportCleanupExecutionOutcome.REMOVED_REMOVE_ONLY


class SonarrQueueHttpClient:
    """Stdlib HTTP client: ``DELETE {base}/api/v3/queue/{id}?removeFromClient=&blocklist=``."""

    def __init__(self, base_url: str, api_key: str, *, timeout_seconds: float = 30.0) -> None:
        self._base = base_url.rstrip("/")
        self._api_key = api_key
        self._timeout = timeout_seconds

    def remove_queue_item(
        self,
        queue_item_id: int,
        *,
        remove_from_client: bool,
        blocklist: bool,
    ) -> None:
        qs = urllib.parse.urlencode(
            {
                "removeFromClient": str(remove_from_client).lower(),
                "blocklist": str(blocklist).lower(),
            },
        )
        url = f"{self._base}/api/v3/queue/{queue_item_id}?{qs}"
        req = urllib.request.Request(
            url,
            method="DELETE",
            headers={"X-Api-Key": self._api_key},
        )
        try:
            with urllib.request.urlopen(req, timeout=self._timeout) as resp:
                if resp.status not in (200, 204):
                    raise RuntimeError(
                        f"Sonarr queue delete unexpected status {resp.status} for {url!r}",
                    )
        except urllib.error.HTTPError as e:
            raise RuntimeError(
                f"Sonarr queue delete failed: HTTP {e.code} for {url!r}",
            ) from e
