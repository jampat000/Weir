"""Subscene subtitle provider (stub — no stable public API wired yet)."""

from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)


def search(
    *,
    query: str,
    season_number: int | None,
    episode_number: int | None,
    languages: list[str],
    media_scope: str,
) -> list[dict[str, Any]]:
    _ = (query, season_number, episode_number, languages, media_scope)
    logger.warning("Subscene provider not yet implemented — returns no results")
    return []


def download(*, subtitle_id: str) -> bytes:
    _ = subtitle_id
    logger.warning("Subscene provider not yet implemented — returns no results")
    return b""
