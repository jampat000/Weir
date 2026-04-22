"""Pure classification of *arr failed-import / rejection status message blobs.

Precedence: any terminal rejection signal beats pending/waiting-only text in the same
blob. Among terminals, the first rule in the app-specific terminal order wins.

Radarr (movies) and Sonarr (TV) share most needles; **quality** rejection phrases differ
because Sonarr commonly references episodes/files with wording Radarr does not use.

This module is *arr-domain rules only* (no queue runtime). Callers consume it via explicit imports.
"""

from __future__ import annotations

import re
from enum import Enum
from typing import Final


class FailedImportOutcome(str, Enum):
    """MediaMop taxonomy for failed-import queue status text.

    The classifier returns eight values. Only the first six map to operator policy
    (``FailedImportCleanupPolicyKey``); see ADR-0010. ``PENDING_WAITING`` and ``UNKNOWN``
    are runtime buckets with **no** persisted per-class action — they must not get UI rows.
    """

    QUALITY = "QUALITY"
    UNMATCHED = "UNMATCHED"
    SAMPLE_RELEASE = "SAMPLE_RELEASE"
    CORRUPT = "CORRUPT"
    DOWNLOAD_FAILED = "DOWNLOAD_FAILED"
    IMPORT_FAILED = "IMPORT_FAILED"
    PENDING_WAITING = "PENDING_WAITING"
    UNKNOWN = "UNKNOWN"


def normalize_failed_import_blob(text: str) -> str:
    """Lowercase and collapse whitespace for deterministic substring checks."""
    s = text.strip().lower()
    return re.sub(r"\s+", " ", s)


def classify_failed_import_message_for_media(blob: str, *, movies: bool) -> FailedImportOutcome:
    """Classify ``blob`` using Radarr-oriented rules when ``movies`` else Sonarr-oriented rules."""
    n = normalize_failed_import_blob(blob)
    if not n:
        return FailedImportOutcome.UNKNOWN

    order = _RADARR_TERMINAL_RULE_ORDER if movies else _SONARR_TERMINAL_RULE_ORDER
    for outcome, needles in order:
        if any(needle in n for needle in needles):
            return outcome

    for needle in _PENDING_PHRASES:
        if needle in n:
            return FailedImportOutcome.PENDING_WAITING

    return FailedImportOutcome.UNKNOWN


def classify_failed_import_message(blob: str) -> FailedImportOutcome:
    """Classify using **Radarr (movies)** rules only.

    Prefer :func:`classify_failed_import_message_for_media` with the correct ``movies`` flag.
    """

    return classify_failed_import_message_for_media(blob, movies=True)


_RADARR_QUALITY: Final[tuple[FailedImportOutcome, tuple[str, ...]]] = (
    FailedImportOutcome.QUALITY,
    ("not an upgrade for existing movie file",),
)

_SONARR_QUALITY: Final[tuple[FailedImportOutcome, tuple[str, ...]]] = (
    FailedImportOutcome.QUALITY,
    (
        "not an upgrade for existing episode file",
        "not an upgrade for existing episode",
        "not an upgrade for existing episodes",
        "not an upgrade for the existing episode",
        "not an upgrade for the existing episodes",
        "not an upgrade for existing",
        "not an upgrade for the existing",
        "existing episode meets cutoff",
        "existing file meets cutoff",
        "not an upgrade for existing movie file",
    ),
)

_UNMATCHED: Final[tuple[FailedImportOutcome, tuple[str, ...]]] = (
    FailedImportOutcome.UNMATCHED,
    ("manual import required",),
)

_SAMPLE_RELEASE: Final[tuple[FailedImportOutcome, tuple[str, ...]]] = (
    FailedImportOutcome.SAMPLE_RELEASE,
    (
        "sample release",
        "release is a sample",
        "because it is a sample",
        "rejected sample",
        "release contains a sample",
        "sample telesync",
        "ts sample",
    ),
)

_CORRUPT: Final[tuple[FailedImportOutcome, tuple[str, ...]]] = (
    FailedImportOutcome.CORRUPT,
    (
        "file is corrupt",
        "corrupt file",
        "corrupt download",
        "unreadable",
        "failed integrity check",
        "checksum failed",
        "hash check failed",
    ),
)

_DOWNLOAD_FAILED: Final[tuple[FailedImportOutcome, tuple[str, ...]]] = (
    FailedImportOutcome.DOWNLOAD_FAILED,
    (
        "download client failed",
        "download failed",
        "failure for usenet download",
        "failure for torrent download",
        "unable to connect to the remote download client",
        "download client unavailable",
        "download client is unavailable",
    ),
)

_IMPORT_FAILED: Final[tuple[FailedImportOutcome, tuple[str, ...]]] = (
    FailedImportOutcome.IMPORT_FAILED,
    (
        "import failed",
        "failed to import",
        "error importing",
        "could not import",
        "couldn't import",
        "not a valid",
    ),
)

_RADARR_TERMINAL_RULE_ORDER: Final[
    tuple[tuple[FailedImportOutcome, tuple[str, ...]], ...]
] = (
    _RADARR_QUALITY,
    _UNMATCHED,
    _SAMPLE_RELEASE,
    _CORRUPT,
    _DOWNLOAD_FAILED,
    _IMPORT_FAILED,
)

_SONARR_TERMINAL_RULE_ORDER: Final[
    tuple[tuple[FailedImportOutcome, tuple[str, ...]], ...]
] = (
    _SONARR_QUALITY,
    _UNMATCHED,
    _SAMPLE_RELEASE,
    _CORRUPT,
    _DOWNLOAD_FAILED,
    _IMPORT_FAILED,
)

_PENDING_PHRASES: Final[tuple[str, ...]] = (
    "downloaded - waiting to import",
    "waiting to import",
    "import pending",
)
