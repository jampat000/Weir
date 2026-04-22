"""Resolve Sonarr/Radarr HTTP base URL + API key: stored (encrypted) row wins when complete, else env."""

from __future__ import annotations

from sqlalchemy.orm import Session

from mediamop.core.config import MediaMopSettings
from mediamop.platform.arr_library.arr_connection_crypto import decrypt_arr_api_key
from mediamop.platform.arr_library.arr_operator_settings_repo import ensure_arr_library_operator_settings_row


def resolve_sonarr_http_credentials(
    session: Session,
    settings: MediaMopSettings,
) -> tuple[str | None, str | None]:
    row = ensure_arr_library_operator_settings_row(session)
    if not bool(row.sonarr_connection_enabled):
        return (None, None)
    url = (row.sonarr_connection_base_url or "").strip()
    ct = (row.sonarr_connection_api_key_ciphertext or "").strip()
    if url and ct:
        key = decrypt_arr_api_key(settings, ct)
        if key:
            return (url, key)
    return settings.arr_http_sonarr_credentials()


def resolve_radarr_http_credentials(
    session: Session,
    settings: MediaMopSettings,
) -> tuple[str | None, str | None]:
    row = ensure_arr_library_operator_settings_row(session)
    if not bool(row.radarr_connection_enabled):
        return (None, None)
    url = (row.radarr_connection_base_url or "").strip()
    ct = (row.radarr_connection_api_key_ciphertext or "").strip()
    if url and ct:
        key = decrypt_arr_api_key(settings, ct)
        if key:
            return (url, key)
    return settings.arr_http_radarr_credentials()


def preview_sonarr_http_credentials_after_put(
    session: Session,
    settings: MediaMopSettings,
    *,
    enabled: bool,
    base_url: str,
    api_key: str,
) -> tuple[str | None, str | None]:
    row = ensure_arr_library_operator_settings_row(session)
    if not enabled:
        return (None, None)
    url = (base_url or "").strip()
    if not url:
        return settings.arr_http_sonarr_credentials()
    key_from_body = (api_key or "").strip()
    if key_from_body:
        return (url, key_from_body)
    ct = (row.sonarr_connection_api_key_ciphertext or "").strip()
    if ct:
        key = decrypt_arr_api_key(settings, ct)
        if key:
            return (url, key)
    return settings.arr_http_sonarr_credentials()


def preview_radarr_http_credentials_after_put(
    session: Session,
    settings: MediaMopSettings,
    *,
    enabled: bool,
    base_url: str,
    api_key: str,
) -> tuple[str | None, str | None]:
    row = ensure_arr_library_operator_settings_row(session)
    if not enabled:
        return (None, None)
    url = (base_url or "").strip()
    if not url:
        return settings.arr_http_radarr_credentials()
    key_from_body = (api_key or "").strip()
    if key_from_body:
        return (url, key_from_body)
    ct = (row.radarr_connection_api_key_ciphertext or "").strip()
    if ct:
        key = decrypt_arr_api_key(settings, ct)
        if key:
            return (url, key)
    return settings.arr_http_radarr_credentials()
