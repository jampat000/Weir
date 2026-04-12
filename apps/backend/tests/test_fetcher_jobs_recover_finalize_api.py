"""``POST /api/v1/fetcher/jobs/{id}/recover-finalize-failure`` — manual ``handler_ok_finalize_failed`` recovery."""

from __future__ import annotations

from datetime import datetime, timedelta, timezone

from sqlalchemy import delete, select
from starlette.testclient import TestClient

from mediamop.core.config import MediaMopSettings
from mediamop.core.db import create_db_engine, create_session_factory
from mediamop.modules.fetcher.failed_import_drive_job_kinds import FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE
from mediamop.modules.fetcher.fetcher_jobs_model import FetcherJob, FetcherJobStatus
from mediamop.modules.fetcher.fetcher_jobs_ops import (
    claim_next_eligible_fetcher_job,
    fail_claimed_fetcher_job,
    fail_leased_fetcher_job_after_complete_failure,
    fetcher_enqueue_or_get_job,
)
from mediamop.platform.activity import constants as act_c
from mediamop.platform.activity.models import ActivityEvent
from tests.integration_helpers import auth_post, csrf as fetch_csrf

import mediamop.modules.fetcher.fetcher_jobs_model  # noqa: F401
import mediamop.platform.activity.models  # noqa: F401
import mediamop.platform.auth.models  # noqa: F401


def _fac():
    settings = MediaMopSettings.load()
    eng = create_db_engine(settings)
    return create_session_factory(eng)


def _t0() -> datetime:
    return datetime(2026, 4, 12, 12, 0, 0, tzinfo=timezone.utc)


def _login_admin(client: TestClient) -> None:
    tok = fetch_csrf(client)
    r = auth_post(
        client,
        "/api/v1/auth/login",
        json={"username": "alice", "password": "test-password-strong", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text


def _recover_url(job_id: int) -> str:
    return f"/api/v1/fetcher/jobs/{job_id}/recover-finalize-failure"


def _seed_finalize_failed_job(*, job_kind: str, dedupe_key: str) -> int:
    t0 = _t0()
    fac = _fac()
    with fac() as db:
        db.execute(delete(ActivityEvent))
        db.execute(delete(FetcherJob))
        db.commit()
    with fac() as db:
        fetcher_enqueue_or_get_job(db, dedupe_key=dedupe_key, job_kind=job_kind)
        db.commit()
    with fac() as db:
        j = claim_next_eligible_fetcher_job(
            db,
            lease_owner="w",
            lease_expires_at=t0 + timedelta(hours=1),
            now=t0,
        )
        assert j is not None
        jid = j.id
        fail_leased_fetcher_job_after_complete_failure(
            db,
            job_id=jid,
            lease_owner="w",
            error_message="fetcher_terminalization_failure: x",
            now=t0,
        )
        db.commit()
    return jid


def test_fetcher_jobs_recover_finalize_success_non_failed_import_kind(client_with_admin: TestClient) -> None:
    jid = _seed_finalize_failed_job(job_kind="k.recover", dedupe_key="recover-api")
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = auth_post(
        client_with_admin,
        _recover_url(jid),
        json={"confirm": True, "csrf_token": tok},
    )
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["job_id"] == jid
    assert body["status"] == FetcherJobStatus.COMPLETED.value
    fac = _fac()
    with fac() as db:
        row = db.get(FetcherJob, jid)
        assert row is not None
        assert row.status == FetcherJobStatus.COMPLETED.value
        ev = db.scalars(
            select(ActivityEvent).where(ActivityEvent.event_type == act_c.FETCHER_JOB_RECOVERED_HANDLER_OK_FINALIZE),
        ).first()
        assert ev is not None
        assert ev.module == "fetcher"
        assert "handler_ok_finalize_failed" in ev.title.lower()
        assert "k.recover" in (ev.detail or "")


def test_fetcher_jobs_recover_finalize_success_failed_import_drive_kind(client_with_admin: TestClient) -> None:
    jid = _seed_finalize_failed_job(
        job_kind=FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
        dedupe_key="recover-fi-radarr",
    )
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = auth_post(
        client_with_admin,
        _recover_url(jid),
        json={"confirm": True, "csrf_token": tok},
    )
    assert r.status_code == 200, r.text
    fac = _fac()
    with fac() as db:
        ev = db.scalars(
            select(ActivityEvent).where(ActivityEvent.event_type == act_c.FETCHER_FAILED_IMPORT_RECOVERED),
        ).first()
        assert ev is not None
        assert "failed-import" in ev.title.lower()
        assert str(jid) in (ev.detail or "")


def test_fetcher_jobs_recover_finalize_409_when_not_finalize_failed(client_with_admin: TestClient) -> None:
    t0 = _t0()
    fac = _fac()
    with fac() as db:
        db.execute(delete(FetcherJob))
        db.commit()
    with fac() as db:
        fetcher_enqueue_or_get_job(db, dedupe_key="recover-bad", job_kind="k", max_attempts=1)
        db.commit()
    with fac() as db:
        j = claim_next_eligible_fetcher_job(
            db,
            lease_owner="w",
            lease_expires_at=t0 + timedelta(hours=1),
            now=t0,
        )
        jid = j.id
        fail_claimed_fetcher_job(
            db,
            job_id=jid,
            lease_owner="w",
            error_message="boom",
            now=t0,
        )
        db.commit()
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = auth_post(
        client_with_admin,
        _recover_url(jid),
        json={"confirm": True, "csrf_token": tok},
    )
    assert r.status_code == 409


def test_fetcher_jobs_recover_finalize_404_unknown_id(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = auth_post(
        client_with_admin,
        _recover_url(999_999),
        json={"confirm": True, "csrf_token": tok},
    )
    assert r.status_code == 404


def test_fetcher_failed_imports_recover_legacy_route_returns_404(client_with_admin: TestClient) -> None:
    """Old failed-import-scoped URL must not answer after the neutral Fetcher jobs move."""

    jid = _seed_finalize_failed_job(job_kind="k.legacy", dedupe_key="recover-legacy-url")
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = auth_post(
        client_with_admin,
        f"/api/v1/fetcher/failed-imports/tasks/{jid}/recover-finalize-failure",
        json={"confirm": True, "csrf_token": tok},
    )
    assert r.status_code == 404
