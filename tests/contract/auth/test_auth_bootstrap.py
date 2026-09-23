"""Bootstrap: creating the first admin, and every way a bootstrap is refused."""

from __future__ import annotations

from collections.abc import Iterator

import pytest

from tests.contract.auth import _helpers as h
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.launcher import ServerUnderTest


def test_bootstrap_allowed_when_no_admin(server_factory, client_factory) -> None:
    sut = server_factory(env={"WEIR_BOOTSTRAP_RATE_MAX_ATTEMPTS": "100", "WEIR_BOOTSTRAP_RATE_WINDOW_SECONDS": "60"})
    c = client_factory(sut)
    r_s = c.get(f"{API}/auth/bootstrap/status")
    assert r_s.status_code == 200
    assert r_s.json()["bootstrap_allowed"] is True
    r_b = c.bootstrap("owner1", "first-owner-pass-min8")
    assert r_b.status_code == 200, r_b.text
    assert r_b.json()["username"] == "owner1"
    assert c.get(f"{API}/auth/bootstrap/status").json()["bootstrap_allowed"] is False
    r_login = h.post_login(c, "owner1", "first-owner-pass-min8")
    assert r_login.status_code == 200, r_login.text
    r_act = c.get(f"{API}/activity/recent")
    assert r_act.status_code == 200, r_act.text
    et = {x["event_type"] for x in r_act.json()["items"]}
    assert "auth.bootstrap_succeeded" in et
    assert "auth.login_succeeded" in et


@pytest.fixture(scope="module")
def no_admin_server(tmp_path_factory: pytest.TempPathFactory) -> Iterator[ServerUnderTest]:
    """No admin, one viewer called ``taken``. Bootstrap tests here must fail, so nothing changes."""

    sut = ServerUnderTest(home=tmp_path_factory.mktemp("weir_home"))
    sut.migrate()
    with seed.stopped(sut, restart=False) as conn:
        seed.insert_user(conn, username="taken", password="irrelevant-password-here", role="viewer")
    sut.start()
    try:
        yield sut
    finally:
        sut.stop()


def test_bootstrap_username_conflict_returns_409(no_admin_server: ServerUnderTest, client_factory) -> None:
    c = client_factory(no_admin_server)
    assert c.get(f"{API}/auth/bootstrap/status").json()["bootstrap_allowed"] is True
    r = c.bootstrap("taken", "valid-pass-bootstrap-8")
    assert r.status_code == 409


def test_bootstrap_rejects_short_password(no_admin_server: ServerUnderTest, client_factory) -> None:
    c = client_factory(no_admin_server)
    assert c.get(f"{API}/auth/bootstrap/status").json()["bootstrap_allowed"] is True
    r = c.bootstrap("owner1", "short")
    assert r.status_code == 422, r.text
    detail = r.json().get("detail") or []
    assert any(
        item.get("loc") == ["body", "password"] and "at least 8 characters" in item.get("msg", "")
        for item in detail
        if isinstance(item, dict)
    )


def test_bootstrap_rejects_common_password(no_admin_server: ServerUnderTest, client_factory) -> None:
    c = client_factory(no_admin_server)
    r = c.bootstrap("owner1", "password1234")
    assert r.status_code == 400, r.text
    assert "common" in r.json()["detail"].lower()


def test_bootstrap_blocked_after_admin_exists(alice: WeirClient) -> None:
    assert alice.get(f"{API}/auth/bootstrap/status").json()["bootstrap_allowed"] is False
    r_b = alice.bootstrap("intruder", "some-long-password-here")
    assert r_b.status_code == 403
