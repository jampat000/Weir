"""Enqueue ``fetcher_jobs`` rows for scheduled and manual Arr search work."""

from __future__ import annotations

import json
import uuid
from collections.abc import Callable

from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.config import MediaMopSettings
from mediamop.modules.fetcher.fetcher_arr_http_resolve import (
    resolve_radarr_http_credentials,
    resolve_sonarr_http_credentials,
)
from mediamop.modules.fetcher.fetcher_arr_operator_settings_prefs import load_fetcher_arr_search_operator_prefs
from mediamop.modules.fetcher.fetcher_jobs_model import FetcherJob
from mediamop.modules.fetcher.fetcher_jobs_ops import fetcher_enqueue_or_get_job, fetcher_enqueue_or_requeue_schedule_job
from mediamop.modules.fetcher.fetcher_search_job_kinds import (
    DEDUPE_SCHEDULED_RADARR_MISSING,
    DEDUPE_SCHEDULED_RADARR_UPGRADE,
    DEDUPE_SCHEDULED_SONARR_MISSING,
    DEDUPE_SCHEDULED_SONARR_UPGRADE,
    JOB_KIND_MISSING_SEARCH_RADARR_MONITORED_MOVIES_V1,
    JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1,
    JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1,
    JOB_KIND_UPGRADE_SEARCH_SONARR_CUTOFF_UNMET_V1,
)

ArrSearchManualScope = str  # sonarr_missing | sonarr_upgrade | radarr_missing | radarr_upgrade


def enqueue_scheduled_sonarr_missing_search_job(session: Session) -> None:
    fetcher_enqueue_or_requeue_schedule_job(
        session,
        dedupe_key=DEDUPE_SCHEDULED_SONARR_MISSING,
        job_kind=JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1,
        payload_json=json.dumps({"manual": False}),
    )


def enqueue_scheduled_sonarr_upgrade_search_job(session: Session) -> None:
    fetcher_enqueue_or_requeue_schedule_job(
        session,
        dedupe_key=DEDUPE_SCHEDULED_SONARR_UPGRADE,
        job_kind=JOB_KIND_UPGRADE_SEARCH_SONARR_CUTOFF_UNMET_V1,
        payload_json=json.dumps({"manual": False}),
    )


def enqueue_scheduled_radarr_missing_search_job(session: Session) -> None:
    fetcher_enqueue_or_requeue_schedule_job(
        session,
        dedupe_key=DEDUPE_SCHEDULED_RADARR_MISSING,
        job_kind=JOB_KIND_MISSING_SEARCH_RADARR_MONITORED_MOVIES_V1,
        payload_json=json.dumps({"manual": False}),
    )


def enqueue_scheduled_radarr_upgrade_search_job(session: Session) -> None:
    fetcher_enqueue_or_requeue_schedule_job(
        session,
        dedupe_key=DEDUPE_SCHEDULED_RADARR_UPGRADE,
        job_kind=JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1,
        payload_json=json.dumps({"manual": False}),
    )


def enqueue_manual_arr_search_job(session: Session, *, scope: ArrSearchManualScope) -> FetcherJob:
    dedupe = f"fetcher.search.manual:{scope}:{uuid.uuid4()}"
    mapping: dict[str, tuple[str, str]] = {
        "sonarr_missing": (JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1, dedupe),
        "sonarr_upgrade": (JOB_KIND_UPGRADE_SEARCH_SONARR_CUTOFF_UNMET_V1, dedupe),
        "radarr_missing": (JOB_KIND_MISSING_SEARCH_RADARR_MONITORED_MOVIES_V1, dedupe),
        "radarr_upgrade": (JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1, dedupe),
    }
    kind, dk = mapping[scope]
    return fetcher_enqueue_or_get_job(
        session,
        dedupe_key=dk,
        job_kind=kind,
        payload_json=json.dumps({"manual": True}),
    )


def arr_search_schedule_specs(
    settings: MediaMopSettings,
    session_factory: sessionmaker[Session],
) -> list[tuple[str, Callable[[Session], None], Callable[[], float]]]:
    """Periodic asyncio specs: (label, enqueue_fn, interval_seconds_getter).

    Lane enable flags and intervals come from the Fetcher SQLite singleton (not env).
    """

    with session_factory() as session:
        prefs = load_fetcher_arr_search_operator_prefs(session)
        son_b, son_k = resolve_sonarr_http_credentials(session, settings)
        rad_b, rad_k = resolve_radarr_http_credentials(session, settings)

    def _iv_getter(lane_attr: str) -> Callable[[], float]:
        def _read() -> float:
            with session_factory() as s:
                p = load_fetcher_arr_search_operator_prefs(s)
            return float(getattr(p, lane_attr).schedule_interval_seconds)

        return _read

    out: list[tuple[str, Callable[[Session], None], Callable[[], float]]] = []
    son_ok = bool(son_b and son_k)
    rad_ok = bool(rad_b and rad_k)
    if son_ok and prefs.sonarr_missing.enabled:
        out.append(("sonarr_missing_search", enqueue_scheduled_sonarr_missing_search_job, _iv_getter("sonarr_missing")))
    if son_ok and prefs.sonarr_upgrade.enabled:
        out.append(("sonarr_upgrade_search", enqueue_scheduled_sonarr_upgrade_search_job, _iv_getter("sonarr_upgrade")))
    if rad_ok and prefs.radarr_missing.enabled:
        out.append(("radarr_missing_search", enqueue_scheduled_radarr_missing_search_job, _iv_getter("radarr_missing")))
    if rad_ok and prefs.radarr_upgrade.enabled:
        out.append(("radarr_upgrade_search", enqueue_scheduled_radarr_upgrade_search_job, _iv_getter("radarr_upgrade")))
    return out
