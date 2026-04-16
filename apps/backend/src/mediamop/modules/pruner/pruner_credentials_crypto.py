"""Fernet encryption for Pruner server credentials JSON (separate pepper from Arr API keys)."""

from __future__ import annotations

import base64
import hashlib

from cryptography.fernet import Fernet, InvalidToken

from mediamop.core.config import MediaMopSettings


def _fernet(settings: MediaMopSettings) -> Fernet | None:
    secret = (settings.session_secret or "").strip()
    if not secret:
        return None
    digest = hashlib.sha256(b"mediamop.pruner.credentials.v1|" + secret.encode()).digest()
    key = base64.urlsafe_b64encode(digest)
    return Fernet(key)


def encrypt_pruner_credentials_json(settings: MediaMopSettings, plaintext_json: str) -> str:
    """Encrypt UTF-8 JSON text for ``pruner_server_instances.credentials_ciphertext``."""

    f = _fernet(settings)
    if f is None:
        msg = "Cannot store Pruner credentials until MEDIAMOP_SESSION_SECRET is set on the server."
        raise ValueError(msg)
    return f.encrypt(plaintext_json.encode("utf-8")).decode("ascii")


def decrypt_pruner_credentials_json(settings: MediaMopSettings, ciphertext: str) -> str | None:
    """Decrypt stored JSON, or ``None`` if secret missing or ciphertext invalid."""

    f = _fernet(settings)
    if f is None:
        return None
    try:
        return f.decrypt(ciphertext.encode("ascii")).decode("utf-8")
    except (InvalidToken, ValueError, TypeError):
        return None
