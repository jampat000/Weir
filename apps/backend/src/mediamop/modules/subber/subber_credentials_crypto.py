"""Fernet encryption for Subber-stored credentials (OpenSubtitles, Sonarr, Radarr)."""

from __future__ import annotations

import base64
import hashlib
import json

from cryptography.fernet import Fernet, InvalidToken

from mediamop.core.config import MediaMopSettings


def _fernet(settings: MediaMopSettings) -> Fernet | None:
    secret = (settings.session_secret or "").strip()
    if not secret:
        return None
    digest = hashlib.sha256(b"mediamop.subber.credentials.v1|" + secret.encode()).digest()
    key = base64.urlsafe_b64encode(digest)
    return Fernet(key)


def encrypt_subber_credentials_json(settings: MediaMopSettings, plaintext_json: str) -> str:
    f = _fernet(settings)
    if f is None:
        msg = "Cannot store Subber credentials until MEDIAMOP_SESSION_SECRET is set on the server."
        raise ValueError(msg)
    return f.encrypt(plaintext_json.encode("utf-8")).decode("ascii")


def decrypt_subber_credentials_json(settings: MediaMopSettings, ciphertext: str) -> str | None:
    f = _fernet(settings)
    if f is None:
        return None
    raw = (ciphertext or "").strip()
    if not raw:
        return None
    try:
        return f.decrypt(raw.encode("ascii")).decode("utf-8")
    except (InvalidToken, ValueError, TypeError):
        return None


def build_provider_credentials_plaintext(provider_key: str, secrets: dict[str, str | None]) -> str:
    """JSON envelope for ``subber_providers.credentials_ciphertext`` (encrypted by caller).

    ``secrets`` keys must match :data:`~mediamop.modules.subber.subber_provider_registry.PROVIDER_CREDENTIAL_FIELDS`.
    """

    from mediamop.modules.subber.subber_provider_registry import PROVIDER_CREDENTIAL_FIELDS

    fields = PROVIDER_CREDENTIAL_FIELDS.get(provider_key, [])
    sec: dict[str, str] = {}
    for k in fields:
        v = secrets.get(k)
        sec[k] = str(v).strip() if v is not None else ""
    return json.dumps({"provider": provider_key, "secrets": sec}, separators=(",", ":"))


def parse_provider_secrets_json(provider_key: str, plaintext: str | None) -> dict[str, str]:
    """Parse decrypted JSON; returns empty strings for missing keys."""

    from mediamop.modules.subber.subber_provider_registry import PROVIDER_CREDENTIAL_FIELDS

    fields = PROVIDER_CREDENTIAL_FIELDS.get(provider_key, [])
    out = {k: "" for k in fields}
    if not plaintext or not plaintext.strip():
        return out
    try:
        data = json.loads(plaintext)
    except json.JSONDecodeError:
        return out
    if not isinstance(data, dict):
        return out
    raw = data.get("secrets")
    if not isinstance(raw, dict):
        return out
    for k in fields:
        out[k] = str(raw.get(k) or "").strip()
    return out
