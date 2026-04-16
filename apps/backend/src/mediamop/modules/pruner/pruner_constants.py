"""Pruner domain constants (scopes, rule families, preview semantics)."""

from __future__ import annotations

MEDIA_SCOPE_TV = "tv"
MEDIA_SCOPE_MOVIES = "movies"

RULE_FAMILY_MISSING_PRIMARY_MEDIA_REPORTED = "missing_primary_media_reported"

# TV preview: one row per **episode** missing a primary image (honest granularity for UI + removal).
# Movies preview: one row per **movie library item** missing a primary image.
