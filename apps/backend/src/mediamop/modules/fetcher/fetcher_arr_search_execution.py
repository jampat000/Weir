"""Tag + command side effects for Arr search jobs (best-effort tags; id-scoped commands)."""

from __future__ import annotations

import logging
from typing import Any

from mediamop.modules.fetcher.fetcher_arr_v3_http import FetcherArrV3Client, FetcherArrV3HttpError

logger = logging.getLogger(__name__)


def _ensure_tag_id(client: FetcherArrV3Client, label: str) -> int:
    wanted = (label or "").strip()
    if not wanted:
        raise ValueError("tag label required")
    tags = client.get_json("/api/v3/tag")
    if isinstance(tags, list):
        for t in tags:
            if not isinstance(t, dict):
                continue
            if str(t.get("label", "")).strip().lower() == wanted.lower():
                tid = t.get("id")
                if isinstance(tid, int) and tid > 0:
                    return tid
    created = client.post_json("/api/v3/tag", {"label": wanted})
    if isinstance(created, dict):
        tid = created.get("id")
        if isinstance(tid, int) and tid > 0:
            return tid
    tags2 = client.get_json("/api/v3/tag")
    if isinstance(tags2, list):
        for t in tags2:
            if isinstance(t, dict) and str(t.get("label", "")).strip().lower() == wanted.lower():
                tid = t.get("id")
                if isinstance(tid, int) and tid > 0:
                    return tid
    raise FetcherArrV3HttpError(f"unable to resolve tag id for {wanted!r}")


def sonarr_series_ids_for_episodes(records: list[dict[str, Any]], *, limit: int) -> list[int]:
    out: list[int] = []
    seen: set[int] = set()
    taken = 0
    for r in records:
        if taken >= limit:
            break
        ep_id = None
        for k in ("id", "episodeId"):
            v = r.get(k)
            if isinstance(v, int) and v > 0:
                ep_id = v
                break
        if ep_id is None:
            continue
        taken += 1
        sid = r.get("seriesId")
        if isinstance(sid, int) and sid > 0 and sid not in seen:
            seen.add(sid)
            out.append(sid)
    return out


def best_effort_tag_sonarr_missing(client: FetcherArrV3Client, episode_records: list[dict[str, Any]]) -> str | None:
    try:
        tag_id = _ensure_tag_id(client, "fetcher-missing")
        sids = sonarr_series_ids_for_episodes(episode_records, limit=len(episode_records))
        if sids:
            client.put_json(
                "/api/v3/series/editor",
                {"seriesIds": sids, "tags": [tag_id], "applyTags": "add"},
            )
    except Exception as e:  # noqa: BLE001
        return f"Sonarr: tag apply warning (fetcher-missing): {e}"
    return None


def best_effort_tag_sonarr_upgrade(client: FetcherArrV3Client, episode_records: list[dict[str, Any]]) -> str | None:
    try:
        tag_id = _ensure_tag_id(client, "fetcher-upgrade")
        sids = sonarr_series_ids_for_episodes(episode_records, limit=len(episode_records))
        if sids:
            client.put_json(
                "/api/v3/series/editor",
                {"seriesIds": sids, "tags": [tag_id], "applyTags": "add"},
            )
    except Exception as e:  # noqa: BLE001
        return f"Sonarr: tag apply warning (fetcher-upgrade): {e}"
    return None


def best_effort_tag_radarr_missing(client: FetcherArrV3Client, movie_ids: list[int]) -> str | None:
    try:
        tag_id = _ensure_tag_id(client, "fetcher-missing")
        if movie_ids:
            client.put_json(
                "/api/v3/movie/editor",
                {"movieIds": movie_ids, "tags": [tag_id], "applyTags": "add"},
            )
    except Exception as e:  # noqa: BLE001
        return f"Radarr: tag apply warning (fetcher-missing): {e}"
    return None


def best_effort_tag_radarr_upgrade(client: FetcherArrV3Client, movie_ids: list[int]) -> str | None:
    try:
        tag_id = _ensure_tag_id(client, "fetcher-upgrade")
        if movie_ids:
            client.put_json(
                "/api/v3/movie/editor",
                {"movieIds": movie_ids, "tags": [tag_id], "applyTags": "add"},
            )
    except Exception as e:  # noqa: BLE001
        return f"Radarr: tag apply warning (fetcher-upgrade): {e}"
    return None


def trigger_sonarr_episode_search(client: FetcherArrV3Client, episode_ids: list[int]) -> None:
    client.post_json("/api/v3/command", {"name": "EpisodeSearch", "episodeIds": episode_ids})


def trigger_radarr_movies_search(client: FetcherArrV3Client, movie_ids: list[int]) -> None:
    client.post_json("/api/v3/command", {"name": "MoviesSearch", "movieIds": movie_ids})


def sonarr_episode_title_lines(records: list[dict[str, Any]]) -> list[str]:
    lines: list[str] = []
    for rec in records:
        series = str(rec.get("seriesTitle") or "").strip()
        sn = rec.get("seasonNumber")
        en = rec.get("episodeNumber")
        et = str(rec.get("title") or "").strip()
        se = ""
        if sn is not None and en is not None:
            try:
                se = f"S{int(sn):02d}E{int(en):02d}"
            except (TypeError, ValueError):
                se = ""
        if series and se and et:
            lines.append(f"{series} {se} - {et}")
        elif series and se:
            lines.append(f"{series} {se}")
        elif series and et:
            lines.append(f"{series} - {et}")
        elif et:
            lines.append(et)
    return [ln for ln in lines if ln]


def radarr_movie_title_lines(records: list[dict[str, Any]]) -> list[str]:
    out: list[str] = []
    for rec in records:
        title = str(rec.get("title") or "").strip() or "Movie"
        year = rec.get("year")
        if year not in (None, ""):
            try:
                out.append(f"{title} ({int(year)})")
            except (TypeError, ValueError):
                out.append(title)
        else:
            out.append(title)
    return out
