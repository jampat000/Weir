"""Canonical ``job_kind`` + dedupe keys for Fetcher Arr search work (SQLite ``fetcher_jobs``)."""

from __future__ import annotations

JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1 = "missing_search.sonarr.monitored_episodes.v1"
JOB_KIND_MISSING_SEARCH_RADARR_MONITORED_MOVIES_V1 = "missing_search.radarr.monitored_movies.v1"
JOB_KIND_UPGRADE_SEARCH_SONARR_CUTOFF_UNMET_V1 = "upgrade_search.sonarr.cutoff_unmet.v1"
JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1 = "upgrade_search.radarr.cutoff_unmet.v1"

FETCHER_ARR_SEARCH_JOB_KINDS: frozenset[str] = frozenset(
    {
        JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1,
        JOB_KIND_MISSING_SEARCH_RADARR_MONITORED_MOVIES_V1,
        JOB_KIND_UPGRADE_SEARCH_SONARR_CUTOFF_UNMET_V1,
        JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1,
    },
)

DEDUPE_SCHEDULED_SONARR_MISSING = "fetcher.search.scheduled:sonarr:missing:v1"
DEDUPE_SCHEDULED_SONARR_UPGRADE = "fetcher.search.scheduled:sonarr:upgrade:v1"
DEDUPE_SCHEDULED_RADARR_MISSING = "fetcher.search.scheduled:radarr:missing:v1"
DEDUPE_SCHEDULED_RADARR_UPGRADE = "fetcher.search.scheduled:radarr:upgrade:v1"
