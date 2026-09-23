"""Sign-in and sign-out, password changes, CSRF on auth routes, the admin check and the login rate limit."""

from __future__ import annotations

import pytest

from tests.contract.auth import _helpers as h
from tests.contract.support.client import ADMIN_PASSWORD, ADMIN_USERNAME, API, WeirClient
from tests.contract.support.launcher import ServerUnderTest

NEW_PASSWORD = "new-password-stronger-123"


@pytest.fixture(scope="module")
def viewer_seeded(server: ServerUnderTest) -> None:
    h.ensure_viewer(server)


def test_login_me_logout_flow(alice: WeirClient) -> None:
    r_login = h.post_login(alice)
    assert r_login.status_code == 200, r_login.text
    assert r_login.json()["user"]["username"] == "alice"
    cookie = alice.cookies.get(h.SESSION_COOKIE)
    assert cookie is not None and len(cookie) > 20

    r_me = alice.get(f"{API}/auth/me")
    assert r_me.status_code == 200
    assert r_me.json()["user"]["username"] == "alice"
    r_session = alice.get(f"{API}/auth/session")
    assert r_session.status_code == 200
    assert r_session.json()["trusted_device"] is False

    r_out = alice.post(f"{API}/auth/logout", headers={"X-CSRF-Token": alice.csrf()})
    assert r_out.status_code == 204, r_out.text

    assert alice.get(f"{API}/auth/me").status_code == 401


def test_login_invalid_password(alice: WeirClient) -> None:
    assert h.post_login(alice, password="wrong-password").status_code == 401


def test_login_rejects_unexpected_fields(alice: WeirClient) -> None:
    r = h.post_login(alice, unexpected="value")
    assert r.status_code == 422


def test_change_password_requires_current_and_forces_new_login(server_factory, client_factory) -> None:
    sut = server_factory()
    c = client_factory(sut)
    h.ensure_admin_account(c)
    assert h.post_login(c).status_code == 200
    r_change = c.post_csrf(
        f"{API}/auth/change-password",
        {"current_password": ADMIN_PASSWORD, "new_password": NEW_PASSWORD},
    )
    assert r_change.status_code == 200, r_change.text
    assert "sign in again" in r_change.json()["message"].lower()
    assert c.get(f"{API}/auth/me").status_code == 401
    assert h.post_login(c).status_code == 401
    r_new = h.post_login(c, password=NEW_PASSWORD)
    assert r_new.status_code == 200, r_new.text


def test_change_password_rejects_wrong_current_password(alice: WeirClient) -> None:
    assert h.post_login(alice).status_code == 200
    r_change = alice.post_csrf(
        f"{API}/auth/change-password",
        {"current_password": "wrong-current-password", "new_password": NEW_PASSWORD},
    )
    assert r_change.status_code == 400
    assert "current password" in (r_change.json().get("detail") or "").lower()


def test_login_invalid_csrf(alice: WeirClient) -> None:
    r = alice.post(
        f"{API}/auth/login",
        json={"username": ADMIN_USERNAME, "password": ADMIN_PASSWORD, "csrf_token": "invalid-token"},
    )
    assert r.status_code == 400


def test_logout_rejects_missing_csrf(alice: WeirClient) -> None:
    h.post_login(alice)
    r = alice.post(f"{API}/auth/logout")
    assert r.status_code == 400


def test_authenticated_csrf_token_is_rejected_across_sessions(
    server: ServerUnderTest, alice: WeirClient, client_factory
) -> None:
    client_a = client_factory(server)
    client_b = client_factory(server)
    assert h.post_login(client_a).status_code == 200
    assert h.post_login(client_b).status_code == 200

    session_a_csrf = client_a.csrf()
    cross_session = client_b.post(
        f"{API}/auth/change-password",
        json={"csrf_token": session_a_csrf, "current_password": ADMIN_PASSWORD, "new_password": NEW_PASSWORD},
    )
    assert cross_session.status_code == 400


def test_admin_ping_requires_admin(alice: WeirClient) -> None:
    h.post_login(alice)
    r = alice.get(f"{API}/auth/admin/ping")
    assert r.status_code == 200
    assert r.json() == {"ok": True}


def test_admin_ping_forbidden_for_viewer(server: ServerUnderTest, viewer_seeded: None, client_factory) -> None:
    c = client_factory(server)
    h.post_login(c, h.VIEWER_USERNAME, h.VIEWER_PASSWORD)
    r = c.get(f"{API}/auth/admin/ping")
    assert r.status_code == 403


def test_login_rate_limited(server_factory, client_factory) -> None:
    sut = server_factory(env={"WEIR_AUTH_LOGIN_RATE_MAX_ATTEMPTS": "3", "WEIR_AUTH_LOGIN_RATE_WINDOW_SECONDS": "120"})
    c = client_factory(sut)
    h.ensure_admin_account(c)
    for _ in range(3):
        r = h.post_login(c, password="wrong")
        assert r.status_code == 401, r.text
    r_limit = h.post_login(c, password="wrong")
    assert r_limit.status_code == 429
    assert "Retry-After" in r_limit.headers
