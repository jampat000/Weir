"""Paged ``GET /api/v3/queue`` for Sonarr/Radarr — shared JSON shape.

Both apps return a paged object with ``records`` and ``totalRecords`` when paging query params
are supplied. This module walks pages until every record is fetched (bounded guard).
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from typing import Any

_QUEUE_PAGE_SIZE = 250
_MAX_QUEUE_PAGES = 400


def fetch_all_v3_queue_records(
    *,
    base_url: str,
    api_key: str,
    app_label: str,
    timeout_seconds: float = 30.0,
) -> list[dict[str, Any]]:
    """Return every queue row dict from ``GET {base}/api/v3/queue`` across all pages."""

    base = base_url.rstrip("/")
    out: list[dict[str, Any]] = []
    page = 1
    while page <= _MAX_QUEUE_PAGES:
        qs = (
            f"page={page}&pageSize={_QUEUE_PAGE_SIZE}"
            "&sortKey=timeleft&sortDirection=ascending"
        )
        url = f"{base}/api/v3/queue?{qs}"
        req = urllib.request.Request(
            url,
            method="GET",
            headers={"X-Api-Key": api_key, "Accept": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=timeout_seconds) as resp:
                raw = resp.read().decode("utf-8")
        except urllib.error.HTTPError as e:
            raise RuntimeError(f"{app_label} queue fetch failed: HTTP {e.code} for {url!r}") from e
        data = json.loads(raw)
        if isinstance(data, list):
            return [x for x in data if isinstance(x, dict)]
        if not isinstance(data, dict):
            return out
        rec = data.get("records")
        if not isinstance(rec, list):
            return out
        chunk = [x for x in rec if isinstance(x, dict)]
        out.extend(chunk)
        total_raw = data.get("totalRecords")
        try:
            total = int(total_raw) if total_raw is not None else len(out)
        except (TypeError, ValueError):
            total = len(out)
        if len(chunk) < _QUEUE_PAGE_SIZE or len(out) >= total:
            break
        page += 1
    return out
