"""Explicit Sonarr/Radarr download-queue handling actions for failed-import rows.

Maps to ``DELETE /api/v3/queue/{id}`` query flags on Sonarr and Radarr **tracked** queue items:

- Sonarr: ``Sonarr.Api.V3/Queue/QueueController.RemoveAction`` → ``Remove(TrackedDownload, …)``
- Radarr: ``Radarr.Api.V3/Queue/QueueController.RemoveAction`` → same structure on ``develop``

Upstream applies ``removeFromClient`` (download client removal) and ``blocklist`` (via
``MarkAsFailed``) independently for tracked rows. MediaMop always sends both booleans explicitly
(lowercase ``true``/``false`` in the query string) so we do not rely on server defaults.

**Pending** queue IDs follow a different upstream branch (``removeFromClient`` is not passed
through); see ADR-0011. ``skipRedownload`` and ``changeCategory`` are omitted (upstream defaults).
"""

from __future__ import annotations

from enum import Enum


class FailedImportQueueHandlingAction(str, Enum):
    """Operator-selected handling for one failed-import *class* (per Sonarr/Radarr axis)."""

    LEAVE_ALONE = "leave_alone"
    REMOVE_ONLY = "remove_only"
    BLOCKLIST_ONLY = "blocklist_only"
    REMOVE_AND_BLOCKLIST = "remove_and_blocklist"

    def removes_from_download_client(self) -> bool:
        return self in (
            FailedImportQueueHandlingAction.REMOVE_ONLY,
            FailedImportQueueHandlingAction.REMOVE_AND_BLOCKLIST,
        )

    def adds_to_blocklist(self) -> bool:
        return self in (
            FailedImportQueueHandlingAction.BLOCKLIST_ONLY,
            FailedImportQueueHandlingAction.REMOVE_AND_BLOCKLIST,
        )


def queue_delete_flags_for_action(action: FailedImportQueueHandlingAction) -> tuple[bool, bool] | None:
    """Return ``(remove_from_client, blocklist)`` for ``DELETE /api/v3/queue/{id}``, or None if no call."""

    if action is FailedImportQueueHandlingAction.LEAVE_ALONE:
        return None
    if action is FailedImportQueueHandlingAction.REMOVE_ONLY:
        return (True, False)
    if action is FailedImportQueueHandlingAction.BLOCKLIST_ONLY:
        return (False, True)
    if action is FailedImportQueueHandlingAction.REMOVE_AND_BLOCKLIST:
        return (True, True)
    raise AssertionError(f"unhandled FailedImportQueueHandlingAction: {action!r}")
