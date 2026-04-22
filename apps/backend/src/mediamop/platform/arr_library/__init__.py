"""Sonarr/Radarr library connection settings and HTTP resolution (SQLite + env fallback).

Persistent operator settings use the ``arr_library_operator_settings`` table.
"""

from __future__ import annotations

from mediamop.platform.arr_library.arr_http_resolve import (
    preview_radarr_http_credentials_after_put,
    preview_sonarr_http_credentials_after_put,
    resolve_radarr_http_credentials,
    resolve_sonarr_http_credentials,
)
from mediamop.platform.arr_library.arr_operator_settings_model import ArrLibraryOperatorSettingsRow
from mediamop.platform.arr_library.arr_operator_settings_repo import ensure_arr_library_operator_settings_row

__all__ = [
    "ArrLibraryOperatorSettingsRow",
    "ensure_arr_library_operator_settings_row",
    "preview_radarr_http_credentials_after_put",
    "preview_sonarr_http_credentials_after_put",
    "resolve_radarr_http_credentials",
    "resolve_sonarr_http_credentials",
]
