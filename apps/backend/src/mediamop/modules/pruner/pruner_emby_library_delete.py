"""Emby library-item removal used by Pruner apply (Emby parity with Jellyfin snapshot-bound slice).

Operator-facing product language: **Remove broken library entries**. Implementation uses Emby’s
REST surface; disk behavior is **not** part of that label.

Verified contract (Emby Server REST, same path/header family as our preview probes for Emby/Jellyfin-style servers):

* **HTTP:** ``DELETE {base_url}/Items/{itemId}``
* **Headers:** ``X-Emby-Token: <api_key>`` (Emby’s documented API key header for authenticated client calls.)
* **What this removes:** The **library item** for that id (episode or movie row, matching preview
  granularity). Emby may also remove or leave underlying media files depending on **server
  library settings** and item type; MediaMop does **not** guarantee metadata-only removal and
  does **not** promise file deletion — operators must rely on Emby’s behavior for their server.
* **What we do not do here:** No Plex calls; Jellyfin uses the sibling module
  :mod:`mediamop.modules.pruner.pruner_jellyfin_library_delete` (same wire protocol, separate docs).

Return values are for Pruner accounting only, not end-user legal guarantees.
"""

from __future__ import annotations

from mediamop.modules.pruner.pruner_http import http_delete, join_base_path


def _headers(api_key: str) -> dict[str, str]:
    return {"X-Emby-Token": api_key, "Accept": "application/json"}


def emby_delete_library_item(*, base_url: str, api_key: str, item_id: str) -> tuple[int, str | None]:
    """Call Emby ``DELETE /Items/{itemId}``. Returns ``(http_status, error_snippet_or_none)``."""

    iid = (item_id or "").strip()
    if not iid:
        return 0, "empty item_id"
    url = join_base_path(base_url, f"Items/{iid}")
    return http_delete(url, headers=_headers(api_key))
