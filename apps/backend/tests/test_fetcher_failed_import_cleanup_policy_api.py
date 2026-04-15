"""GET/PUT ``/api/v1/fetcher/failed-imports/cleanup-policy``."""

from __future__ import annotations

from sqlalchemy import delete
from starlette.testclient import TestClient

from mediamop.core.config import MediaMopSettings
from mediamop.core.db import create_db_engine, create_session_factory
from mediamop.modules.fetcher.cleanup_policy_model import FetcherFailedImportCleanupPolicyRow

from tests.integration_helpers import auth_post, csrf as fetch_csrf, trusted_browser_origin_headers

_LA = "leave_alone"
_RO = "remove_only"


def _axis(
    *,
    quality: str = _LA,
    unmatched: str = _LA,
    sample: str = _LA,
    corrupt: str = _LA,
    download: str = _LA,
    import_: str = _LA,
    cleanup_drive_schedule_enabled: bool = False,
    cleanup_drive_schedule_interval_seconds: int = 3600,
) -> dict[str, object]:
    return {
        "handling_quality_rejection": quality,
        "handling_unmatched_manual_import": unmatched,
        "handling_sample_release": sample,
        "handling_corrupt_import": corrupt,
        "handling_failed_download": download,
        "handling_failed_import": import_,
        "cleanup_drive_schedule_enabled": cleanup_drive_schedule_enabled,
        "cleanup_drive_schedule_interval_seconds": cleanup_drive_schedule_interval_seconds,
    }


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
        assert "handling_quality_rejection" in d
        assert "handling_unmatched_manual_import" in d
        assert "handling_sample_release" in d
        assert "handling_corrupt_import" in d
        assert "handling_failed_download" in d
        assert "handling_failed_import" in d
        assert "cleanup_drive_schedule_enabled" in d
        assert "cleanup_drive_schedule_interval_seconds" in d


def test_fetcher_cleanup_policy_put_then_get_database_source(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    payload = {
        "csrf_token": tok,
        "movies": _axis(quality=_RO, import_=_RO, cleanup_drive_schedule_enabled=False),
        "tv_shows": _axis(corrupt=_RO, cleanup_drive_schedule_enabled=True, cleanup_drive_schedule_interval_seconds=1800),
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
    assert out["movies"]["handling_quality_rejection"] == _RO
    assert out["movies"]["handling_failed_import"] == _RO
    assert out["tv_shows"]["handling_corrupt_import"] == _RO
    assert out["movies"]["cleanup_drive_schedule_enabled"] is False
    assert out["tv_shows"]["cleanup_drive_schedule_enabled"] is True
    assert out["tv_shows"]["cleanup_drive_schedule_interval_seconds"] == 1800

    r2 = client_with_admin.get("/api/v1/fetcher/failed-imports/cleanup-policy")
    assert r2.status_code == 200
    assert "source" not in r2.json()
    assert r2.json()["movies"]["handling_quality_rejection"] == _RO


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
        "movies": _axis(),
        "tv_shows": _axis(),
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
        "movies": _axis(),
        "tv_shows": _axis(),
    }
    r = client_with_admin.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy",
        json=payload,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r.status_code == 400


def test_fetcher_cleanup_policy_put_axis_tv_shows_only_preserves_movies(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    seed = {
        "csrf_token": tok,
        "movies": _axis(quality=_RO),
        "tv_shows": _axis(),
    }
    r0 = client_with_admin.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy",
        json=seed,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r0.status_code == 200, r0.text
    movies_before = r0.json()["movies"]

    tok2 = fetch_csrf(client_with_admin)
    tv_next = {**r0.json()["tv_shows"], "handling_corrupt_import": _RO, "csrf_token": tok2}
    r_axis = client_with_admin.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy/tv-shows",
        json=tv_next,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r_axis.status_code == 200, r_axis.text
    out = r_axis.json()
    assert out["movies"] == movies_before
    assert out["tv_shows"]["handling_corrupt_import"] == _RO


def test_fetcher_cleanup_policy_put_axis_movies_only_preserves_tv_shows(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    seed = {
        "csrf_token": tok,
        "movies": _axis(
            unmatched=_RO,
            cleanup_drive_schedule_enabled=True,
            cleanup_drive_schedule_interval_seconds=7200,
        ),
        "tv_shows": _axis(quality=_RO),
    }
    r0 = client_with_admin.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy",
        json=seed,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r0.status_code == 200, r0.text
    tv_before = r0.json()["tv_shows"]

    tok2 = fetch_csrf(client_with_admin)
    mov_next = {**r0.json()["movies"], "handling_failed_import": _RO, "csrf_token": tok2}
    r_axis = client_with_admin.put(
        "/api/v1/fetcher/failed-imports/cleanup-policy/movies",
        json=mov_next,
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r_axis.status_code == 200, r_axis.text
    out = r_axis.json()
    assert out["tv_shows"] == tv_before
    assert out["movies"]["handling_failed_import"] == _RO
