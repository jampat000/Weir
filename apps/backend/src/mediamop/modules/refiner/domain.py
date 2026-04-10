"""Refiner domain: pure ownership vs upstream blocking (no *arr HTTP, no orchestration).

Callers (future integration) map Radarr/Sonarr queue JSON into :class:`RefinerQueueRowView`.
Title/path matching belongs in a separate layer so anchor-weighted rules can replace
string heuristics without changing this contract.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Sequence


@dataclass(frozen=True, slots=True)
class RefinerQueueRowView:
    """One upstream queue row after the caller has decided file association.

    - ``applies_to_file``: True when this row claims the candidate file (path/id/title
      association — decided outside this module). Must be True for import-pending rows
      that own the file so ownership never depends on ``is_upstream_active``.
    - ``is_upstream_active``: True when the row is still in-flight / unsettled for
      *blocking* only (e.g. downloading). Inactive rows may still own.
    - ``is_import_pending``: Radarr/Sonarr import-pending style state; informational
      and for tests — ownership uses ``applies_to_file`` only, never ``is_upstream_active``.
    - ``blocking_suppressed_for_import_wait``: deadlock escape: row still owns but must
      not contribute to blocking (e.g. “waiting to import” with no eligible files).
    """

    applies_to_file: bool
    is_upstream_active: bool
    is_import_pending: bool
    blocking_suppressed_for_import_wait: bool = False


def file_is_owned_by_queue(rows: Sequence[RefinerQueueRowView]) -> bool:
    """True if any queue row claims the file, in any upstream state.

    import-pending rows count as ownership when ``applies_to_file`` is True.
    This function does not inspect ``is_upstream_active`` (that is blocking-only).
    """
    return any(r.applies_to_file for r in rows)


def should_block_for_upstream(rows: Sequence[RefinerQueueRowView]) -> bool:
    """True if any applicable row says processing should wait on upstream.

    Only uses activity/blocking fields — never re-derives ownership.
    """
    return any(
        r.applies_to_file
        and r.is_upstream_active
        and not r.blocking_suppressed_for_import_wait
        for r in rows
    )
