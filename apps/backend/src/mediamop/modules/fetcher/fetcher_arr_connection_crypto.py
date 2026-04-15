"""Encrypt/decrypt Sonarr/Radarr API keys at rest (Fernet key derived from ``MEDIAMOP_SESSION_SECRET``)."""

from __future__ import annotations

import base64
import hashlib

from cryptography.fernet import Fernet, InvalidToken

from mediamop.core.config import MediaMopSettings


def _fernet(settings: MediaMopSettings) -> Fernet | None:
    secret = (settings.session_secret or "").strip()
    if not secret:
        return None
    digest = hashlib.sha256(b"mediamop.fetcher.arr_api_key.v1|" + secret.encode()).digest()
    key = base64.urlsafe_b64encode(digest)
    return Fernet(key)


def encrypt_arr_api_key(settings: MediaMopSettings, plaintext: str) -> str:
    """Encrypt API key for SQLite storage. Requires a configured session secret."""

    f = _fernet(settings)
    if f is None:
        msg = "Cannot save library API keys until MEDIAMOP_SESSION_SECRET is set on the server."
        raise ValueError(msg)
    return f.encrypt(plaintext.strip().encode("utf-8")).decode("ascii")


def decrypt_arr_api_key(settings: MediaMopSettings, ciphertext: str) -> str | None:
    """Decrypt stored API key, or ``None`` if secret missing or ciphertext invalid."""

    f = _fernet(settings)
    if f is None:
        return None
    try:
        return f.decrypt(ciphertext.encode("ascii")).decode("utf-8")
    except (InvalidToken, ValueError, TypeError):
        return None
