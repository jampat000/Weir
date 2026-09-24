"""Sign-in sessions: the cookie, trusted devices, restarts, the five-session limit, expiry and cleanup."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

from tests.contract.auth import _helpers as h
from tests.contract.support import seed
from tests.contract.support.client import ADMIN_USERNAME, API, WeirClient
from tests.contract.support.launcher import ServerUnderTest

# Sessions that are already past their absolute expiry are deleted at server start, so a test that needs
# one alive at startup and expired at request time seeds it to expire shortly after the restart.
_EXPIRY_MARGIN = timedelta(seconds=15)


def test_session_rotation_replaces_old_cookie(alice: WeirClient) -> None:
    h.post_login(alice)
    old_cookie = alice.cookies.get(h.SESSION_COOKIE)
    h.post_login(alice)
    new_cookie = alice.cookies.get(h.SESSION_COOKIE)
    assert old_cookie != new_cookie
    assert alice.get(f"{API}/auth/me").status_code == 200


def test_login_cookie_has_explicit_lifetime(alice: WeirClient) -> None:
    response = h.post_login(alice)
    assert response.status_code == 200, response.text
    set_cookie = h.set_cookie_header(response)
    assert "Max-Age=" in set_cookie
    assert "HttpOnly" in set_cookie
    assert "SameSite=" in set_cookie


def test_trusted_device_login_uses_extended_session_policy(
    server: ServerUnderTest, alice: WeirClient, client_factory
) -> None:
    response = h.post_login(alice, trusted_device=True)
    assert response.status_code == 200, response.text

    session_response = alice.get(f"{API}/auth/session")
    assert session_response.status_code == 200, session_response.text
    body = session_response.json()
    assert body["trusted_device"] is True
    assert body["idle_timeout_minutes"] == 60 * 1440
    assert body["absolute_timeout_days"] == 365
    session_id = body["session_id"]

    with seed.stopped(server) as conn:
        newest = seed.rows(conn, "SELECT id, is_trusted_device FROM user_sessions ORDER BY created_at DESC LIMIT 1")
    assert newest and newest[0]["is_trusted_device"] == 1
    assert newest[0]["id"].replace("-", "") == session_id.replace("-", "")


def test_session_cookie_survives_backend_app_restart(
    server: ServerUnderTest, alice: WeirClient, client_factory
) -> None:
    login = h.post_login(alice)
    assert login.status_code == 200, login.text
    cookie = alice.cookies.get(h.SESSION_COOKIE)
    assert cookie

    server.restart()

    after = h.client_with_cookie(client_factory, server, cookie)
    me = after.get(f"{API}/auth/me")
    assert me.status_code == 200, me.text
    assert me.json()["user"]["username"] == "alice"


def test_second_browser_login_does_not_revoke_first_browser_session(
    server: ServerUnderTest, alice: WeirClient, client_factory
) -> None:
    remote_client = client_factory(server)
    local_client = client_factory(server)
    assert h.post_login(remote_client).status_code == 200
    assert remote_client.get(f"{API}/auth/me").status_code == 200

    assert h.post_login(local_client).status_code == 200
    assert local_client.get(f"{API}/auth/me").status_code == 200
    assert remote_client.get(f"{API}/auth/me").status_code == 200


def test_login_keeps_only_newest_five_active_sessions(server_factory, client_factory) -> None:
    sut = server_factory()
    # ensure_admin_account's own bootstrap sign-in (#704) is itself a session, so it is one of the
    # seven vying for the newest-5 slots below: it and the oldest of the six logins get revoked.
    bootstrap_session_id = h.ensure_admin_account(client_factory(sut))
    clients = [client_factory(sut) for _ in range(6)]
    for c in clients:
        login = h.post_login(c)
        assert login.status_code == 200, login.text

    assert clients[0].get(f"{API}/auth/me").status_code == 401
    for c in clients[1:]:
        assert c.get(f"{API}/auth/me").status_code == 200

    with seed.stopped(sut, restart=False) as conn:
        active = seed.scalar(conn, "SELECT COUNT(*) FROM user_sessions WHERE revoked_at IS NULL")
        revoked_ids = [r["id"] for r in seed.rows(conn, "SELECT id FROM user_sessions WHERE revoked_at IS NOT NULL")]
    assert active == 5
    assert len(revoked_ids) == 2
    assert bootstrap_session_id in revoked_ids


def test_session_limit_ignores_absolute_expired_sessions(server_factory, client_factory) -> None:
    sut = server_factory()
    bootstrap_session_id = h.ensure_admin_account(client_factory(sut))
    now = datetime.now(UTC)
    expires = now + _EXPIRY_MARGIN
    with seed.stopped(sut) as conn:
        uid = h.user_id(conn, ADMIN_USERNAME)
        expired_ids = [
            h.insert_session(
                conn,
                user=uid,
                created_at=now - timedelta(minutes=10 - i),
                absolute_expires_at=expires,
                last_seen_at=now,
            )[0]
            for i in range(5)
        ]
    h.wait_past(expires)

    live = client_factory(sut)
    assert h.post_login(live).status_code == 200
    assert live.get(f"{API}/auth/me").status_code == 200

    with seed.stopped(sut, restart=False) as conn:
        rows = seed.rows(conn, "SELECT id, revoked_at FROM user_sessions")
    by_id = {r["id"]: r for r in rows}
    assert set(expired_ids) <= set(by_id), "the expired sessions were cleaned up before the login ran"
    # ensure_admin_account's own sign-in (#704) is a session too, live and unexpired just like the
    # one this test's own login creates; both are excluded from the deliberately-expired set here.
    other_ids = set(expired_ids) | {bootstrap_session_id}
    live_rows = [r for sid, r in by_id.items() if sid not in other_ids]
    assert len(live_rows) == 1
    assert live_rows[0]["revoked_at"] is None
    assert all(by_id[sid]["revoked_at"] is None for sid in expired_ids)


def test_load_valid_session_throttles_last_seen_persistence(server_factory, client_factory) -> None:
    """Avoid persisting last_seen on every authenticated read (SQLite write pressure)."""

    sut = server_factory(env={"WEIR_SESSION_IDLE_MINUTES": "720"})
    h.ensure_admin_account(client_factory(sut))
    now = datetime.now(UTC)
    recent_seen = now
    old_seen = now - timedelta(minutes=10)
    with seed.stopped(sut) as conn:
        uid = h.user_id(conn, ADMIN_USERNAME)
        recent_id, recent_raw = h.insert_session(
            conn, user=uid, created_at=old_seen, absolute_expires_at=now + timedelta(days=1), last_seen_at=recent_seen
        )
        old_id, old_raw = h.insert_session(
            conn, user=uid, created_at=old_seen, absolute_expires_at=now + timedelta(days=1), last_seen_at=old_seen
        )

    # Well inside the 60 s touch gap for the recent session; well past it for the old one.
    assert h.client_with_cookie(client_factory, sut, recent_raw).get(f"{API}/auth/me").status_code == 200
    assert h.client_with_cookie(client_factory, sut, old_raw).get(f"{API}/auth/me").status_code == 200

    with seed.stopped(sut, restart=False) as conn:
        seen = {r["id"]: r["last_seen_at"] for r in seed.rows(conn, "SELECT id, last_seen_at FROM user_sessions")}
    assert h.parse_utc_text(seen[recent_id]) == recent_seen
    assert h.parse_utc_text(seen[old_id]) > old_seen + timedelta(minutes=5)


def test_expired_session_is_rejected_and_revoked(server_factory, client_factory) -> None:
    sut = server_factory(env={"WEIR_SESSION_IDLE_MINUTES": "720"})
    h.ensure_admin_account(client_factory(sut))
    now = datetime.now(UTC)
    expires = now + _EXPIRY_MARGIN
    with seed.stopped(sut) as conn:
        uid = h.user_id(conn, ADMIN_USERNAME)
        sid, raw = h.insert_session(conn, user=uid, created_at=now, absolute_expires_at=expires, last_seen_at=now)
    h.wait_past(expires)

    expired = h.client_with_cookie(client_factory, sut, raw)
    assert expired.get(f"{API}/auth/me").status_code == 401
    # A 401 rolls the request's writes back, so the revocation is persisted by a request that loads the
    # session and still succeeds: the CSRF fetch every page makes first (it falls back to an anonymous token).
    assert expired.get(f"{API}/auth/csrf").status_code == 200

    with seed.stopped(sut, restart=False) as conn:
        rows = seed.rows(conn, "SELECT revoked_at FROM user_sessions WHERE id = ?", (sid,))
    assert rows, "the session was cleaned up before the request ran"
    assert rows[0]["revoked_at"] is not None


def test_session_cleanup_deletes_revoked_and_expired_sessions(server_factory, client_factory) -> None:
    """The cleanup runs at server start (and hourly); a restart is the black-box trigger."""

    sut = server_factory(env={"WEIR_SESSION_IDLE_MINUTES": "720"})
    # ensure_admin_account's own bootstrap sign-in (#704) is itself a still-live, unexpired session,
    # alongside the ones seeded directly below; cleanup must leave both it and the active one, and
    # delete only the revoked and expired rows.
    bootstrap_session_id = h.ensure_admin_account(client_factory(sut))
    now = datetime.now(UTC)
    with seed.stopped(sut) as conn:
        uid = h.user_id(conn, ADMIN_USERNAME)
        later = now + timedelta(days=1)
        h.insert_session(conn, user=uid, created_at=now, absolute_expires_at=later, last_seen_at=now, revoked_at=now)
        h.insert_session(
            conn, user=uid, created_at=now, absolute_expires_at=now - timedelta(seconds=1), last_seen_at=now
        )
        active_id, _ = h.insert_session(conn, user=uid, created_at=now, absolute_expires_at=later, last_seen_at=now)

    with seed.stopped(sut, restart=False) as conn:
        ids = [r["id"] for r in seed.rows(conn, "SELECT id FROM user_sessions")]
    assert set(ids) == {active_id, bootstrap_session_id}
