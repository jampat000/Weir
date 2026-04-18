"""Supported Subber subtitle provider keys and metadata for UI + validation."""

from __future__ import annotations

PROVIDER_OPENSUBTITLES_ORG = "opensubtitles_org"
PROVIDER_OPENSUBTITLES_COM = "opensubtitles_com"
PROVIDER_PODNAPISI = "podnapisi"
PROVIDER_SUBSCENE = "subscene"
PROVIDER_ADDIC7ED = "addic7ed"

ALL_PROVIDER_KEYS: tuple[str, ...] = (
    PROVIDER_OPENSUBTITLES_ORG,
    PROVIDER_OPENSUBTITLES_COM,
    PROVIDER_PODNAPISI,
    PROVIDER_SUBSCENE,
    PROVIDER_ADDIC7ED,
)

PROVIDER_DISPLAY_NAMES: dict[str, str] = {
    PROVIDER_OPENSUBTITLES_ORG: "OpenSubtitles.org",
    PROVIDER_OPENSUBTITLES_COM: "OpenSubtitles.com",
    PROVIDER_PODNAPISI: "Podnapisi",
    PROVIDER_SUBSCENE: "Subscene",
    PROVIDER_ADDIC7ED: "Addic7ed",
}

PROVIDER_REQUIRES_ACCOUNT: dict[str, bool] = {
    PROVIDER_OPENSUBTITLES_ORG: True,
    PROVIDER_OPENSUBTITLES_COM: True,
    PROVIDER_PODNAPISI: False,
    PROVIDER_SUBSCENE: False,
    PROVIDER_ADDIC7ED: True,
}

PROVIDER_CREDENTIAL_FIELDS: dict[str, list[str]] = {
    PROVIDER_OPENSUBTITLES_ORG: ["username", "password", "api_key"],
    PROVIDER_OPENSUBTITLES_COM: ["username", "password", "api_key"],
    PROVIDER_PODNAPISI: ["username", "password"],
    PROVIDER_SUBSCENE: [],
    PROVIDER_ADDIC7ED: ["username", "password"],
}
