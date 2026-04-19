"""Gestdown subtitle client (TV only) — proxy for Addic7ed via gestdown.info API."""

from __future__ import annotations

import json
import logging
import urllib.parse
import urllib.request
from typing import Any

BASE = "https://api.gestdown.info"
USER_AGENT = "MediaMop/1.0"
logger = logging.getLogger(__name__)


def _get(path: str) -> dict[str, Any] | list[Any] | None:
    url = f"{BASE}{path}"
    req = urllib.request.Request(  # noqa: S310
        url,
        headers={"User-Agent": USER_AGENT, "Accept": "application/json"},
        method="GET",
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read().decode("utf-8", errors="replace")
            if not raw.strip():
                return None
            parsed = json.loads(raw)
            return parsed if isinstance(parsed, (dict, list)) else None
    except Exception:
        logger.exception("Gestdown request failed: %s", url)
        return None


def search(
    *,
    query: str,
    season_number: int | None,
    episode_number: int | None,
    languages: list[str],
) -> list[dict[str, Any]]:
    """Search Gestdown for TV subtitles. Returns list of result dicts."""
    encoded = urllib.parse.quote(query.strip())
    body = _get(f"/shows/search/{encoded}")
    if not isinstance(body, dict):
        return []
    shows = body.get("shows") or body.get("results") or []
    if not isinstance(shows, list) or not shows:
        return []
    show_id = None
    for show in shows:
        if isinstance(show, dict):
            sid = show.get("id") or show.get("uniqueId") or show.get("showId")
            if sid:
                show_id = str(sid)
                break
    if not show_id or season_number is None or episode_number is None:
        return []
    lang = (languages[0] if languages else "en").lower()[:2]
    path = f"/subtitles/get/{urllib.parse.quote(show_id)}/{int(season_number)}/{int(episode_number)}/{urllib.parse.quote(lang)}"
    result = _get(path)
    if not isinstance(result, dict):
        return []
    subs = result.get("subtitles") or result.get("data") or []
    if not isinstance(subs, list):
        return []
    out = []
    for s in subs:
        if not isinstance(s, dict):
            continue
        dl = s.get("downloadUri") or s.get("download") or s.get("url") or ""
        if dl:
            out.append({"download_url": str(dl), "language": lang, "hearing_impaired": bool(s.get("hearingImpaired"))})
    return out


def download(*, download_url: str) -> bytes:
    """Download SRT directly from Gestdown download URL."""
    url = download_url if download_url.startswith("http") else f"{BASE}{download_url}"
    req = urllib.request.Request(  # noqa: S310
        url,
        headers={"User-Agent": USER_AGENT},
        method="GET",
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.read()
