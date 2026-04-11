"""Operator POST for manual Fetcher Arr search enqueue (``fetcher_jobs`` only)."""

from __future__ import annotations

from sqlalchemy import delete, select
from starlette.testclient import TestClient

from mediamop.core.config import MediaMopSettings
from mediamop.core.db import create_db_engine, create_session_factory
from mediamop.modules.fetcher.fetcher_jobs_model import FetcherJob
from mediamop.modules.fetcher.fetcher_search_job_kinds import (
    JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1,
    JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1,
)
from tests.integration_helpers import auth_post, csrf as fetch_csrf

import mediamop.modules.fetcher.fetcher_jobs_model  # noqa: F401
import mediamop.modules.refiner.jobs_model  # noqa: F401
import mediamop.platform.activity.models  # noqa: F401
import mediamop.platform.auth.models  # noqa: F401


def _fac():
    settings = MediaMopSettings.load()
    eng = create_db_engine(settings)
    return create_session_factory(eng)


def _clear_fetcher_jobs() -> None:
    fac = _fac()
    with fac() as db:
        db.execute(delete(FetcherJob))
        db.commit()


def _login_admin(client: TestClient) -> None:
    tok = fetch_csrf(client)
    r = auth_post(
        client,
        "/api/v1/auth/login",
        json={"username": "alice", "password": "test-password-strong", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text


def _login_viewer(client: TestClient) -> None:
    tok = fetch_csrf(client)
    r = auth_post(
        client,
        "/api/v1/auth/login",
        json={"username": "bob", "password": "viewer-password-here", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text


def test_fetcher_arr_search_enqueue_admin_ok(client_with_admin: TestClient) -> None:
    _clear_fetcher_jobs()
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = auth_post(
        client_with_admin,
        "/api/v1/fetcher/arr-search/enqueue",
        json={"scope": "sonarr_missing", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["ok"] is True
    assert body["job_kind"] == JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1
    assert body["dedupe_key"].startswith("fetcher.search.manual:sonarr_missing:")
    fac = _fac()
    with fac() as db:
        row = db.scalars(select(FetcherJob).where(FetcherJob.id == body["job_id"])).first()
        assert row is not None
        assert row.job_kind == JOB_KIND_MISSING_SEARCH_SONARR_MONITORED_EPISODES_V1


def test_fetcher_arr_search_enqueue_viewer_forbidden(client_with_viewer: TestClient) -> None:
    _clear_fetcher_jobs()
    _login_viewer(client_with_viewer)
    tok = fetch_csrf(client_with_viewer)
    r = auth_post(
        client_with_viewer,
        "/api/v1/fetcher/arr-search/enqueue",
        json={"scope": "radarr_upgrade", "csrf_token": tok},
    )
    assert r.status_code == 403, r.text


def test_fetcher_arr_search_enqueue_radarr_upgrade_kind(client_with_admin: TestClient) -> None:
    _clear_fetcher_jobs()
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = auth_post(
        client_with_admin,
        "/api/v1/fetcher/arr-search/enqueue",
        json={"scope": "radarr_upgrade", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text
    assert r.json()["job_kind"] == JOB_KIND_UPGRADE_SEARCH_RADARR_CUTOFF_UNMET_V1
