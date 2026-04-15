"""Selection + cooldown for Arr search jobs.

Cooldown rows are keyed by ``(app, action, item_type, item_id)`` where:

- ``app`` is ``sonarr`` or ``radarr`` (no shared Arr-wide bucket),
- ``action`` is ``missing`` or ``upgrade`` (no cross-family coupling),

so the four lanes
``(sonarr, missing)``, ``(radarr, missing)``, ``(sonarr, upgrade)``, ``(radarr, upgrade)``
are independent. Retry windows and schedule gates are configured per lane in
:class:`~mediamop.core.config.MediaMopSettings` (not Fetcher-style shared cooldown).
"""

from __future__ import annotations

import logging
from collections.abc import Iterator
from datetime import datetime, timedelta, timezone
from typing import Any

from sqlalchemy import delete, select
from sqlalchemy.orm import Session

from mediamop.modules.fetcher.fetcher_arr_operator_settings_prefs import FetcherArrSearchOperatorPrefs
from mediamop.modules.fetcher.fetcher_arr_action_log_model import FetcherArrActionLog
from mediamop.modules.fetcher.fetcher_arr_v3_http import FetcherArrV3Client

logger = logging.getLogger(__name__)

_PAGINATE_WANTED_MAX_PAGES = 250


def _safe_int(v: object) -> int | None:
    try:
        n = int(str(v).strip())
        return n if n > 0 else None
    except Exception:
        return None


def _extract_first_int(rec: dict[str, Any], *keys: str) -> int | None:
    for k in keys:
        v = rec.get(k)
        if isinstance(v, int) and v > 0:
            return v
        if isinstance(v, str) and v.isdigit():
            n = int(v)
            if n > 0:
                return n
    return None


def _take_records_and_ids(
    records: list[dict[str, Any]], *keys: str, limit: int
) -> tuple[list[int], list[dict[str, Any]]]:
    ids_out: list[int] = []
    recs_out: list[dict[str, Any]] = []
    seen: set[int] = set()
    for r in records:
        rid = _extract_first_int(r, *keys)
        if rid is None or rid in seen:
            continue
        seen.add(rid)
        ids_out.append(rid)
        recs_out.append(r)
        if len(ids_out) >= limit:
            break
    return ids_out, recs_out


def prune_fetcher_arr_action_log(
    session: Session,
    *,
    prefs: FetcherArrSearchOperatorPrefs,
    now: datetime,
) -> None:
    """Drop stale cooldown rows per lane using that lane's retry minutes (no cross-lane window)."""

    lanes: list[tuple[str, str, int]] = [
        ("sonarr", "missing", int(prefs.sonarr_missing.retry_delay_minutes)),
        ("sonarr", "upgrade", int(prefs.sonarr_upgrade.retry_delay_minutes)),
        ("radarr", "missing", int(prefs.radarr_missing.retry_delay_minutes)),
        ("radarr", "upgrade", int(prefs.radarr_upgrade.retry_delay_minutes)),
    ]
    for app, action, minutes in lanes:
        window_minutes = max(1, min(minutes, 365 * 24 * 60)) * 2
        cutoff = now - timedelta(minutes=window_minutes)
        session.execute(
            delete(FetcherArrActionLog).where(
                FetcherArrActionLog.app == app,
                FetcherArrActionLog.action == action,
                FetcherArrActionLog.created_at < cutoff,
            ),
        )


def filter_item_ids_by_cooldown(
    session: Session,
    *,
    app: str,
    action: str,
    item_type: str,
    ids: list[int],
    cooldown_minutes: int,
    now: datetime,
    max_apply: int | None = None,
) -> list[int]:
    """Return ids with no recent log row for the same lane ``(app, action, item_type, item_id)``.

    ``app`` ∈ ``{sonarr, radarr}``, ``action`` ∈ ``{missing, upgrade}`` — four isolated lanes.
    """

    if not ids:
        return []
    cooldown_minutes = max(1, int(cooldown_minutes or 60))
    window_start = now - timedelta(minutes=cooldown_minutes)
    recent_q = session.scalars(
        select(FetcherArrActionLog.item_id).where(
            FetcherArrActionLog.app == app,
            FetcherArrActionLog.action == action,
            FetcherArrActionLog.item_type == item_type,
            FetcherArrActionLog.item_id.in_(ids),
            FetcherArrActionLog.created_at >= window_start,
        ),
    )
    recent_ids = {int(x) for x in recent_q.all()}
    allowed = [i for i in ids if i not in recent_ids]
    if max_apply is not None and max_apply >= 0:
        allowed = allowed[: int(max_apply)]
    if allowed:
        session.add_all(
            [
                FetcherArrActionLog(
                    created_at=now,
                    app=app,
                    action=action,
                    item_type=item_type,
                    item_id=int(i),
                )
                for i in allowed
            ],
        )
    return allowed


def paginate_wanted_cutoff(
    client: FetcherArrV3Client,
    session: Session,
    *,
    app: str,
    action: str,
    item_type: str,
    id_keys: tuple[str, ...],
    limit: int,
    cooldown_minutes: int,
    now: datetime,
) -> tuple[list[int], list[dict[str, Any]], int]:
    limit = max(1, int(limit))
    page_size = min(100, max(50, limit))
    allowed_ids: list[int] = []
    allowed_recs: list[dict[str, Any]] = []
    seen: set[int] = set()
    total_records = 0
    page = 1
    while len(allowed_ids) < limit and page <= _PAGINATE_WANTED_MAX_PAGES:
        data = client.get_json("/api/v3/wanted/cutoff", params={"page": page, "pageSize": page_size})
        if not isinstance(data, dict):
            break
        records = data.get("records") or []
        if page == 1:
            total_records = int(data.get("totalRecords") or 0)
        if not records:
            break
        ids_page, recs_page = _take_records_and_ids(records, *id_keys, limit=len(records))
        candidates = [(i, r) for i, r in zip(ids_page, recs_page, strict=False) if i not in seen]
        if not candidates:
            page += 1
            continue
        batch_ids = [i for i, _ in candidates]
        need = limit - len(allowed_ids)
        newly = filter_item_ids_by_cooldown(
            session,
            app=app,
            action=action,
            item_type=item_type,
            ids=batch_ids,
            cooldown_minutes=cooldown_minutes,
            now=now,
            max_apply=need,
        )
        new_set = set(newly)
        for i, r in candidates:
            if i in new_set:
                seen.add(i)
                allowed_ids.append(i)
                allowed_recs.append(r)
                if len(allowed_ids) >= limit:
                    break
        page += 1
    return allowed_ids, allowed_recs, total_records


def iter_sonarr_monitored_missing_episodes(client: FetcherArrV3Client) -> Iterator[dict[str, Any]]:
    series_list = client.get_json("/api/v3/series")
    if not isinstance(series_list, list):
        return
    for series in sorted((s for s in series_list if isinstance(s, dict)), key=lambda s: _safe_int(s.get("id")) or 0):
        sid = _safe_int(series.get("id"))
        if not sid:
            continue
        episodes = client.get_json("/api/v3/episode", params={"seriesId": sid})
        if not isinstance(episodes, list):
            continue
        for episode in episodes:
            if not isinstance(episode, dict):
                continue
            if not bool(episode.get("monitored")):
                continue
            if bool(episode.get("hasFile")):
                continue
            yield episode


def iter_radarr_monitored_missing_movies(client: FetcherArrV3Client) -> Iterator[dict[str, Any]]:
    movies = client.get_json("/api/v3/movie")
    if not isinstance(movies, list):
        return
    for movie in sorted((m for m in movies if isinstance(m, dict)), key=lambda m: _safe_int(m.get("id")) or 0):
        if not bool(movie.get("monitored")):
            continue
        if bool(movie.get("hasFile")):
            continue
        yield movie


def drain_monitored_missing_with_cooldown(
    client: FetcherArrV3Client,
    session: Session,
    *,
    entries: Iterator[dict[str, Any]],
    id_keys: tuple[str, ...],
    app: str,
    action: str,
    item_type: str,
    limit: int,
    cooldown_minutes: int,
    now: datetime,
) -> tuple[list[int], list[dict[str, Any]], int]:
    limit = max(1, int(limit))
    page_size = min(100, max(50, limit))
    allowed_ids: list[int] = []
    allowed_recs: list[dict[str, Any]] = []
    seen: set[int] = set()
    total_candidates = 0
    buffer: list[tuple[int, dict[str, Any]]] = []

    def flush_buffer() -> None:
        nonlocal buffer
        if not buffer:
            return
        need = limit - len(allowed_ids)
        if need <= 0:
            return
        batch = buffer
        buffer = []
        batch_ids = [i for i, _ in batch]
        newly = filter_item_ids_by_cooldown(
            session,
            app=app,
            action=action,
            item_type=item_type,
            ids=batch_ids,
            cooldown_minutes=cooldown_minutes,
            now=now,
            max_apply=need,
        )
        new_set = set(newly)
        for i, r in batch:
            if i in new_set:
                allowed_ids.append(i)
                allowed_recs.append(r)
                if len(allowed_ids) >= limit:
                    break

    for raw in entries:
        total_candidates += 1
        eid = _extract_first_int(raw, *id_keys)
        if eid is None or eid in seen:
            continue
        seen.add(eid)
        if len(allowed_ids) >= limit:
            continue
        buffer.append((eid, raw))
        if len(buffer) >= page_size:
            flush_buffer()

    if buffer and len(allowed_ids) < limit:
        flush_buffer()

    logger.info(
        "%s monitored-missing pool=%d after_cooldown_dispatch=%d",
        app,
        total_candidates,
        len(allowed_ids),
    )
    return allowed_ids, allowed_recs, total_candidates


def wanted_queue_total_records(client: FetcherArrV3Client, *, kind: str) -> int:
    path = "/api/v3/wanted/missing" if kind == "missing" else "/api/v3/wanted/cutoff"
    data = client.get_json(path, params={"page": 1, "pageSize": 50})
    if not isinstance(data, dict):
        return 0
    return int(data.get("totalRecords") or 0)


def utc_now() -> datetime:
    return datetime.now(timezone.utc)
