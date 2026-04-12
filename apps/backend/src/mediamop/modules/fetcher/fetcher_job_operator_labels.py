"""Short operator-facing labels for ``fetcher_jobs.job_kind`` (aligned with web ``fetcherJobKindOperatorLabel``)."""

from __future__ import annotations

from mediamop.modules.fetcher.failed_import_drive_job_kinds import (
    FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
    FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE,
)
from mediamop.modules.fetcher.fetcher_search_job_kinds import (
    JOB_KIND_MISSING_SEARCH_RADARR_MONITORED_MOVIES_V1,
    JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1,
    JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1,
    JOB_KIND_UPGRADE_SEARCH_SONARR_CUTOFF_UNMET_V1,
)

_LABEL_BY_KIND: dict[str, str] = {
    FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE: "Radarr cleanup",
    FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE: "Sonarr cleanup",
    JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1: "Sonarr missing search",
    JOB_KIND_MISSING_SEARCH_RADARR_MONITORED_MOVIES_V1: "Radarr missing search",
    JOB_KIND_UPGRADE_SEARCH_SONARR_CUTOFF_UNMET_V1: "Sonarr upgrade search",
    JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1: "Radarr upgrade search",
}


def fetcher_job_kind_operator_label(job_kind: str) -> str:
    """Return a short label for known production kinds; otherwise the raw ``job_kind`` string."""

    return _LABEL_BY_KIND.get(job_kind, job_kind)
