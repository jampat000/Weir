"""``fetcher_job_kind_operator_label`` stays aligned with known production ``job_kind`` strings."""

from __future__ import annotations

from mediamop.modules.fetcher.failed_import_drive_job_kinds import FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE
from mediamop.modules.fetcher.fetcher_job_operator_labels import fetcher_job_kind_operator_label
from mediamop.modules.fetcher.fetcher_search_job_kinds import JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1


def test_fetcher_job_kind_operator_label_known_failed_import_drive() -> None:
    assert fetcher_job_kind_operator_label(FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE) == "Radarr cleanup"


def test_fetcher_job_kind_operator_label_known_arr_search() -> None:
    assert fetcher_job_kind_operator_label(JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1) == "Sonarr missing search"


def test_fetcher_job_kind_operator_label_unknown_passthrough() -> None:
    assert fetcher_job_kind_operator_label("custom.unknown.v1") == "custom.unknown.v1"
