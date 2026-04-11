"""GET/PUT ``/api/v1/fetcher/failed-imports/cleanup-policy``."""

from __future__ import annotations

from sqlalchemy import delete
from starlette.testclient import TestClient

from mediamop.core.config import MediaMopSettings
from mediamop.core.db import create_db_engine, create_session_factory
from mediamop.modules.fetcher.cleanup_policy_model import FetcherFailedImportCleanupPolicyRow

from tests.integration_helpers import auth_post, csrf as fetch_csrf, trusted_browser_origin_headers


def _clear_policy_row() -> None:
    settings = MediaMopSettings.load()
    fac = create_session_factory(create_db_engine(settings))
    with fac() as db:
        db.execute(delete(FetcherFailedImportCleanupPolicyRow))
        db.commit()


def _login_admin(client: TestClient) -> None:
    tok = fetch_csrf(client)
    r = auth_post(
        client,
        "/api/v1/auth/login",
        json={"username": "alice", "password": "test-password-strong", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text


def test_fetcher_cleanup_policy_get_requires_auth(client_with_admin: TestClient) -> None:
    r = client_with_admin.get("/api/v1/fetcher/failed-imports/cleanup-policy")
    assert r.status_code == 401


def test_fetcher_cleanup_policy_get_seeds_row_and_returns_db_payload(client_with_admin: TestClient) -> None:
    _clear_policy_row()
    _login_admin(client_with_admin)
    r = client_with_admin.get("/api/v1/fetcher/failed-imports/cleanup-policy")
    assert r.status_code == 200, r.text
    body = r.json()
    assert "source" not in body
    assert body["updated_at"] is not None
    for axis in ("movies", "tv_shows"):
        d = body[axis]
        assert "remove_quality_rejections" in d
        assert "remove_unmatched_manual_import_rejections" in d
        assert "remove_corrupt_imports" in d
        assert "remove_failed_downloads" in d
        assert "remove_failed_imports" in d


def test_fetcher_cleanup_policy_put_then_get_database_source(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    payload = {
        "csrf_token": tok,
        "movies": {
            "remove_quality_rejections": True,
            "remove_unmatched_manual_import_rejections": False,
            "remove_corrupt_imports": False,
            "remove_failed_downloads": False,
            "remove_failed_imports": True,
        },
        "tv_shows": {
            "remove_quality_rejections": False,
            "remove_unmatched_manual_import_rejections": False,
            "remove_corrupt_imports": True,
            "remove_failed_downloads": False,
            "remove_failed_imports": False,
        },
    }
    r = client_with_admin.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy",
        json=payload,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r.status_code == 200, r.text
    out = r.json()
    assert "source" not in out
    assert out["updated_at"] is not None
    assert out["movies"]["remove_quality_rejections"] is True
    assert out["movies"]["remove_failed_imports"] is True
    assert out["tv_shows"]["remove_corrupt_imports"] is True

    r2 = client_with_admin.get("/api/v1/fetcher/failed-imports/cleanup-policy")
    assert r2.status_code == 200
    assert "source" not in r2.json()
    assert r2.json()["movies"]["remove_quality_rejections"] is True


def test_fetcher_cleanup_policy_put_viewer_403(client_with_viewer: TestClient) -> None:
    tok = fetch_csrf(client_with_viewer)
    r_login = auth_post(
        client_with_viewer,
        "/api/v1/auth/login",
        json={"username": "bob", "password": "viewer-password-here", "csrf_token": tok},
    )
    assert r_login.status_code == 200, r_login.text
    tok2 = fetch_csrf(client_with_viewer)
    payload = {
        "csrf_token": tok2,
        "movies": {
            "remove_quality_rejections": True,
            "remove_unmatched_manual_import_rejections": False,
            "remove_corrupt_imports": False,
            "remove_failed_downloads": False,
            "remove_failed_imports": False,
        },
        "tv_shows": {
            "remove_quality_rejections": False,
            "remove_unmatched_manual_import_rejections": False,
            "remove_corrupt_imports": False,
            "remove_failed_downloads": False,
            "remove_failed_imports": False,
        },
    }
    r = client_with_viewer.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy",
        json=payload,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r.status_code == 403


def test_fetcher_cleanup_policy_put_invalid_csrf(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    payload = {
        "csrf_token": "bad",
        "movies": {
            "remove_quality_rejections": False,
            "remove_unmatched_manual_import_rejections": False,
            "remove_corrupt_imports": False,
            "remove_failed_downloads": False,
            "remove_failed_imports": False,
        },
        "tv_shows": {
            "remove_quality_rejections": False,
            "remove_unmatched_manual_import_rejections": False,
            "remove_corrupt_imports": False,
            "remove_failed_downloads": False,
            "remove_failed_imports": False,
        },
    }
    r = client_with_admin.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy",
        json=payload,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r.status_code == 400

