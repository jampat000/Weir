"""Emby / Jellyfin / Plex library probes for Pruner (connection test + missing-primary preview)."""

from __future__ import annotations

import json
import urllib.error
from typing import Any

from mediamop.modules.pruner.pruner_constants import MEDIA_SCOPE_MOVIES, MEDIA_SCOPE_TV
from mediamop.modules.pruner.pruner_http import http_get_json, http_get_text, join_base_path


def _emby_style_headers(api_key: str) -> dict[str, str]:
    return {"X-Emby-Token": api_key, "Accept": "application/json"}


def test_emby_jellyfin_connection(*, base_url: str, api_key: str = "") -> tuple[bool, str]:
    """Minimal anonymous ping (``/System/Info/Public``) — ``api_key`` reserved for future stricter checks."""

    del api_key

    url = join_base_path(base_url, "System/Info/Public")
    try:
        status, data = http_get_json(url, headers={"Accept": "application/json"})
    except urllib.error.URLError as e:
        return False, f"network error: {e}"
    except Exception as e:  # noqa: BLE001 — surface to operator
        return False, f"error: {e}"
    if status != 200:
        return False, f"HTTP {status}"
    if not isinstance(data, dict):
        return False, "unexpected non-object JSON"
    name = data.get("ServerName") or data.get("ProductName") or "server"
    ver = data.get("Version") or data.get("ProductVersion") or "?"
    return True, f"{name} ({ver})"


def test_plex_connection(*, base_url: str, auth_token: str | None) -> tuple[bool, str]:
    """Minimal Plex ping: ``GET /identity`` (local servers; optional ``X-Plex-Token``)."""

    url = join_base_path(base_url, "identity")
    headers: dict[str, str] = {"Accept": "application/xml"}
    if auth_token:
        headers["X-Plex-Token"] = auth_token
    try:
        status, text = http_get_text(url, headers=headers)
    except urllib.error.URLError as e:
        return False, f"network error: {e}"
    except Exception as e:  # noqa: BLE001
        return False, f"error: {e}"
    if status != 200:
        return False, f"HTTP {status}"
    snippet = text[:2000].lower()
    if "media" not in snippet and "plex" not in snippet:
        return False, "unexpected identity payload (not Plex?)"
    return True, "Plex identity OK"


def _items_page(
    *,
    base_url: str,
    api_key: str,
    include_item_types: str,
    start_index: int,
    page_limit: int,
    use_has_primary_image: bool,
) -> dict[str, Any] | None:
    params: dict[str, str] = {
        "Recursive": "true",
        "IncludeItemTypes": include_item_types,
        "StartIndex": str(start_index),
        "Limit": str(page_limit),
    }
    if use_has_primary_image:
        params["HasPrimaryImage"] = "false"
    url = join_base_path(base_url, "Items", params)
    status, data = http_get_json(url, headers=_emby_style_headers(api_key))
    if status != 200:
        msg = f"Items query failed HTTP {status}"
        raise RuntimeError(msg)
    if not isinstance(data, dict):
        msg = "Items query returned non-object JSON"
        raise RuntimeError(msg)
    return data


def _item_missing_primary(item: dict[str, Any]) -> bool:
    if item.get("ImageTags") and isinstance(item["ImageTags"], dict) and item["ImageTags"].get("Primary"):
        return False
    if item.get("PrimaryImageItemId"):
        return False
    return True


def list_missing_primary_candidates(
    *,
    base_url: str,
    api_key: str,
    media_scope: str,
    max_items: int,
) -> tuple[list[dict[str, Any]], bool]:
    """Return candidate dicts for TV (episodes) or Movies (movie items), newest-first pages.

    ``truncated`` is True when more rows existed beyond ``max_items``.
    """

    if media_scope == MEDIA_SCOPE_TV:
        include_types = "Episode"
    elif media_scope == MEDIA_SCOPE_MOVIES:
        include_types = "Movie"
    else:
        msg = f"unsupported media_scope: {media_scope!r}"
        raise ValueError(msg)

    use_filter = True
    candidates: list[dict[str, Any]] = []
    start = 0
    page = min(100, max(1, max_items))
    total_hits: int | None = None

    while len(candidates) < max_items:
        try:
            data = _items_page(
                base_url=base_url,
                api_key=api_key,
                include_item_types=include_types,
                start_index=start,
                page_limit=page,
                use_has_primary_image=use_filter,
            )
        except urllib.error.HTTPError as e:
            if e.code == 400 and use_filter:
                use_filter = False
                start = 0
                candidates.clear()
                total_hits = None
                continue
            raise
        items = data.get("Items")
        if not isinstance(items, list):
            break
        if "TotalRecordCount" in data and isinstance(data["TotalRecordCount"], int):
            total_hits = int(data["TotalRecordCount"])

        for it in items:
            if not isinstance(it, dict):
                continue
            if not use_filter and not _item_missing_primary(it):
                continue
            if media_scope == MEDIA_SCOPE_TV:
                candidates.append(
                    {
                        "granularity": "episode",
                        "item_id": str(it.get("Id", "")),
                        "series_name": it.get("SeriesName") or it.get("Album") or "",
                        "season_number": it.get("ParentIndexNumber"),
                        "episode_number": it.get("IndexNumber"),
                        "episode_title": it.get("Name") or "",
                    },
                )
            else:
                candidates.append(
                    {
                        "granularity": "movie_item",
                        "item_id": str(it.get("Id", "")),
                        "title": it.get("Name") or "",
                        "year": it.get("ProductionYear"),
                    },
                )
            if len(candidates) >= max_items:
                break

        fetched = len(items)
        start += fetched
        if fetched == 0:
            break

    truncated = False
    if total_hits is not None and len(candidates) < total_hits and len(candidates) >= max_items:
        truncated = True
    elif total_hits is not None and start < total_hits and len(candidates) >= max_items:
        truncated = True

    return candidates, truncated


def plex_preview_unsupported_detail() -> str:
    return (
        "Plex: candidate preview for missing primary art is not implemented in this release "
        "(connection test is supported). TV scope elsewhere is episode-level only."
    )


def preview_payload_json(
    *,
    provider: str,
    base_url: str,
    media_scope: str,
    secrets: dict[str, str],
    max_items: int,
) -> tuple[str, str, list[dict[str, Any]], bool]:
    """Returns ``(outcome, unsupported_detail_or_empty, candidates, truncated)``."""

    if provider == "plex":
        return "unsupported", plex_preview_unsupported_detail(), [], False
    api_key = secrets.get("api_key", "")
    cands, trunc = list_missing_primary_candidates(
        base_url=base_url,
        api_key=api_key,
        media_scope=media_scope,
        max_items=max_items,
    )
    return "success", "", cands, trunc


def serialize_candidates(candidates: list[dict[str, Any]]) -> str:
    return json.dumps(candidates, separators=(",", ":"))[:8_000_000]
