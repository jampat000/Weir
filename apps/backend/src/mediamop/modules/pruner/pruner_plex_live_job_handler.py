"""Handler for ``pruner.candidate_removal.plex_live.v1`` (retired).

This job kind previously scanned Plex at execution time without a preview snapshot. Plex
``missing_primary_media_reported`` now uses the same preview → apply-from-preview model as
Jellyfin/Emby. Queued rows of this kind fail loudly so operators are not left with a silent no-op.

``MEDIAMOP_PRUNER_PLEX_LIVE_REMOVAL_ENABLED`` is deprecated and is no longer consulted here.
"""

from __future__ import annotations

import json
from collections.abc import Callable
from typing import Any

from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.config import MediaMopSettings
from mediamop.modules.pruner.worker_loop import PrunerJobWorkContext


def _parse_payload(payload_json: str | None) -> dict[str, Any]:
    if not payload_json or not payload_json.strip():
        msg = "plex live job requires payload_json"
        raise ValueError(msg)
    data = json.loads(payload_json)
    if not isinstance(data, dict):
        msg = "plex live payload must be a JSON object"
        raise ValueError(msg)
    return data


def make_pruner_plex_live_removal_handler(
    settings: MediaMopSettings,
    session_factory: sessionmaker[Session],
) -> Callable[[PrunerJobWorkContext], None]:
    del settings, session_factory

    def _run(ctx: PrunerJobWorkContext) -> None:
        _parse_payload(ctx.payload_json)
        msg = (
            "pruner.candidate_removal.plex_live.v1 is retired: Plex Remove broken library entries uses "
            "missing-primary preview snapshots and apply-from-preview only. "
            "MEDIAMOP_PRUNER_PLEX_LIVE_REMOVAL_ENABLED is deprecated and ignored for this path."
        )
        raise RuntimeError(msg)

    return _run
