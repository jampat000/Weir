"""Pruner domain constants (scopes, rule families, preview semantics)."""

from __future__ import annotations

MEDIA_SCOPE_TV = "tv"
MEDIA_SCOPE_MOVIES = "movies"

RULE_FAMILY_MISSING_PRIMARY_MEDIA_REPORTED = "missing_primary_media_reported"

# Scheduled preview interval (seconds) — per ``pruner_scope_settings`` row, clamped on write.
PRUNER_SCHEDULED_PREVIEW_INTERVAL_MIN_SECONDS = 60
PRUNER_SCHEDULED_PREVIEW_INTERVAL_MAX_SECONDS = 86_400


def clamp_pruner_scheduled_preview_interval_seconds(raw: int) -> int:
    """Operator-facing cadence floor/ceiling for ``scheduled_preview_interval_seconds``."""

    return max(
        PRUNER_SCHEDULED_PREVIEW_INTERVAL_MIN_SECONDS,
        min(PRUNER_SCHEDULED_PREVIEW_INTERVAL_MAX_SECONDS, int(raw)),
    )

# TV preview: one row per **episode** missing a primary image (honest granularity for UI + removal).
# Movies preview: one row per **movie library item** missing a primary image.
