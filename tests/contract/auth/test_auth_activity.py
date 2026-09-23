"""What sign-in, sign-out and refused bootstraps record in Activity, and how repeats are throttled."""

from __future__ import annotations

from tests.contract.auth import _helpers as h
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.launcher import ServerUnderTest


def _count_events(conn, event_type: str, detail: str | None = None) -> int:
    if detail is None:
        return int(seed.scalar(conn, "SELECT COUNT(*) FROM activity_events WHERE event_type = ?", (event_type,)))
    return int(
        seed.scalar(
            conn,
            "SELECT COUNT(*) FROM activity_events WHERE event_type = ? AND detail = ?",
            (event_type, detail),
        )
    )


def test_login_failed_persisted_throttled_per_username(
    server: ServerUnderTest, alice: WeirClient, client_factory
) -> None:
    with seed.stopped(server) as conn:
        conn.execute("DELETE FROM activity_events WHERE event_type = 'auth.login_failed' AND detail = 'alice'")
        before = _count_events(conn, "auth.login_failed", "alice")
    c = client_factory(server)
    for _ in range(3):
        assert h.post_login(c, password="wrong-password").status_code == 401
    with seed.stopped(server) as conn:
        after = _count_events(conn, "auth.login_failed", "alice")
    assert after - before == 1


def test_bootstrap_denied_persisted_throttled(server: ServerUnderTest, alice: WeirClient, client_factory) -> None:
    with seed.stopped(server) as conn:
        conn.execute("DELETE FROM activity_events WHERE event_type = 'auth.bootstrap_denied'")
        before = _count_events(conn, "auth.bootstrap_denied")
    c = client_factory(server)
    assert c.bootstrap("intruder", "some-long-password-here").status_code == 403
    assert c.bootstrap("intruder2", "other-long-password-here").status_code == 403
    with seed.stopped(server) as conn:
        after = _count_events(conn, "auth.bootstrap_denied")
    assert after - before == 1


def test_activity_recent_requires_authentication(alice: WeirClient) -> None:
    assert alice.get(f"{API}/activity/recent").status_code == 401


def test_activity_recent_includes_login_event(alice: WeirClient) -> None:
    r_login = h.post_login(alice)
    assert r_login.status_code == 200, r_login.text
    r_act = alice.get(f"{API}/activity/recent")
    assert r_act.status_code == 200, r_act.text
    items = r_act.json()["items"]
    assert any(x.get("event_type") == "auth.login_succeeded" and x.get("detail") == "alice" for x in items)


def test_activity_recent_includes_logout_event(alice: WeirClient) -> None:
    h.post_login(alice)
    r_out = alice.post(f"{API}/auth/logout", headers={"X-CSRF-Token": alice.csrf()})
    assert r_out.status_code == 204, r_out.text
    h.post_login(alice)
    r_act = alice.get(f"{API}/activity/recent")
    assert r_act.status_code == 200
    types = [x["event_type"] for x in r_act.json()["items"]]
    assert "auth.login_succeeded" in types
    assert "auth.logout" in types
