"""Apply Broker indexer rows to Sonarr/Radarr via ``/api/v3/indexer`` (Broker-owned)."""

from __future__ import annotations

import json
from typing import Any

from mediamop.modules.broker.broker_arr_http import BrokerArrV3Client, BrokerArrV3HttpError
from mediamop.modules.broker.broker_indexers_model import BrokerIndexerRow

MANAGED_NAME_PREFIX = "Broker|"


def arr_indexer_display_name(slug: str) -> str:
    return f"{MANAGED_NAME_PREFIX}{slug}"


def slug_from_managed_arr_indexer(name: str) -> str | None:
    if not name.startswith(MANAGED_NAME_PREFIX):
        return None
    rest = name[len(MANAGED_NAME_PREFIX) :].strip()
    return rest or None


def _categories_list(row: BrokerIndexerRow) -> list[int]:
    try:
        raw = json.loads(row.categories or "[]")
    except json.JSONDecodeError:
        return []
    if not isinstance(raw, list):
        return []
    out: list[int] = []
    for x in raw:
        try:
            out.append(int(x))
        except (TypeError, ValueError):
            continue
    return out


def _torznab_body(row: BrokerIndexerRow) -> dict[str, Any]:
    return {
        "enableRss": True,
        "enableAutomaticSearch": True,
        "enableInteractiveSearch": True,
        "supportsRss": True,
        "supportsSearch": True,
        "protocol": "torrent",
        "priority": int(row.priority),
        "name": arr_indexer_display_name(row.slug),
        "fields": [
            {"name": "baseUrl", "value": row.url},
            {"name": "apiPath", "value": "/api"},
            {"name": "apiKey", "value": row.api_key},
            {"name": "categories", "value": _categories_list(row)},
        ],
        "implementationName": "Torznab",
        "implementation": "Torznab",
        "configContract": "TorznabSettings",
        "tags": [],
    }


def _newznab_body(row: BrokerIndexerRow) -> dict[str, Any]:
    return {
        "enableRss": True,
        "enableAutomaticSearch": True,
        "enableInteractiveSearch": True,
        "supportsRss": True,
        "supportsSearch": True,
        "protocol": "usenet",
        "priority": int(row.priority),
        "name": arr_indexer_display_name(row.slug),
        "fields": [
            {"name": "baseUrl", "value": row.url},
            {"name": "apiPath", "value": "/api"},
            {"name": "apiKey", "value": row.api_key},
            {"name": "categories", "value": _categories_list(row)},
        ],
        "implementationName": "Newznab",
        "implementation": "Newznab",
        "configContract": "NewznabSettings",
        "tags": [],
    }


def broker_row_to_arr_indexer_body(row: BrokerIndexerRow) -> dict[str, Any]:
    proto = (row.protocol or "torrent").strip().lower()
    if proto == "usenet":
        return _newznab_body(row)
    return _torznab_body(row)


def _index_managed_indexers(arr_list: list[Any]) -> dict[str, dict[str, Any]]:
    out: dict[str, dict[str, Any]] = {}
    for it in arr_list:
        if not isinstance(it, dict):
            continue
        name = str(it.get("name") or "")
        slug = slug_from_managed_arr_indexer(name)
        if slug:
            out[slug] = it
    return out


def apply_broker_indexers_to_arr(client: BrokerArrV3Client, enabled_rows: list[BrokerIndexerRow]) -> None:
    """Full sync: remove managed indexers no longer desired; add or update the rest."""

    raw = client.get_json("/api/v3/indexer")
    if not isinstance(raw, list):
        raise BrokerArrV3HttpError("invalid /api/v3/indexer response")

    desired = {r.slug: r for r in enabled_rows}
    managed = _index_managed_indexers(raw)

    for slug, item in list(managed.items()):
        if slug not in desired:
            iid = item.get("id")
            if iid is not None:
                client.delete_json(f"/api/v3/indexer/{int(iid)}")

    raw2 = client.get_json("/api/v3/indexer")
    if not isinstance(raw2, list):
        raise BrokerArrV3HttpError("invalid /api/v3/indexer response after deletes")
    managed2 = _index_managed_indexers(raw2)

    for slug, row in desired.items():
        body = broker_row_to_arr_indexer_body(row)
        existing = managed2.get(slug)
        if existing is None:
            client.post_json("/api/v3/indexer", body)
        else:
            merged: dict[str, Any] = dict(existing)
            for k, v in body.items():
                if k != "id":
                    merged[k] = v
            merged["id"] = existing.get("id")
            client.put_json(f"/api/v3/indexer/{int(existing['id'])}", merged)
