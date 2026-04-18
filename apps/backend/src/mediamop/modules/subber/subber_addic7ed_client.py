"""Addic7ed subtitle provider (stub — TV-focused; no stable API wired yet)."""

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
    username: str | None = None,
    password: str | None = None,
) -> list[dict[str, Any]]:
    _ = (query, season_number, episode_number, languages, username, password)
    if media_scope != "tv":
        return []
    logger.warning("Addic7ed provider not yet implemented — returns no results")
    return []


def download(*, subtitle_id: str, username: str | None = None, password: str | None = None) -> bytes:
    _ = (subtitle_id, username, password)
    logger.warning("Addic7ed provider not yet implemented — returns no results")
    return b""
