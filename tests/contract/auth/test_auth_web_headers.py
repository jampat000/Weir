"""Security headers on the API, and how the bundled web app and its static assets are served."""

from __future__ import annotations

from collections.abc import Iterator

import pytest

from tests.contract.auth import _helpers as h
from tests.contract.support.client import API
from tests.contract.support.launcher import ServerUnderTest


@pytest.fixture(scope="module")
def web_server(tmp_path_factory: pytest.TempPathFactory) -> Iterator[ServerUnderTest]:
    dist = h.write_web_dist(
        tmp_path_factory.mktemp("web") / "dist",
        "<!doctype html><html><body><div id='root'>Weir</div></body></html>",
    )
    (dist / "assets").mkdir()
    (dist / "assets" / "app.js").write_text("console.log('ok');", encoding="utf-8")
    env = {"WEIR_SECURITY_ENABLE_HSTS": "1", "WEIR_WEB_DIST": str(dist)}
    with h.own_server(tmp_path_factory, env) as sut:
        yield sut


def test_security_headers_on_health_and_api(web_server: ServerUnderTest, client_factory) -> None:
    c = client_factory(web_server)
    r_h = c.get("/health")
    assert r_h.headers.get("X-Content-Type-Options") == "nosniff"
    assert r_h.headers.get("X-Frame-Options") == "DENY"
    assert r_h.headers.get("Referrer-Policy") == "strict-origin-when-cross-origin"
    assert r_h.headers.get("Content-Security-Policy")
    assert r_h.headers.get("Cache-Control", "").startswith("no-store")
    assert r_h.headers.get("strict-transport-security")
    r_csrf = c.get(f"{API}/auth/csrf")
    assert r_csrf.headers.get("Content-Security-Policy")
    assert "frame-ancestors" in (r_csrf.headers.get("Content-Security-Policy") or "").lower()
    assert r_csrf.headers.get("Cache-Control", "").startswith("no-store")
    for path in (
        f"{API}/system/directories",
        f"{API}/suite/security-overview",
        f"{API}/suite/update-status",
        "/metrics",
    ):
        r = c.get(path)
        assert r.headers.get("Cache-Control", "").startswith("no-store"), path


def test_static_assets_do_not_get_api_no_store(web_server: ServerUnderTest, client_factory) -> None:
    response = client_factory(web_server).get("/assets/app.js")
    assert response.status_code == 200
    assert response.headers.get("Cache-Control") != "no-store, private"


def test_bundled_html_csp_does_not_allow_inline_styles(web_server: ServerUnderTest, client_factory) -> None:
    response = client_factory(web_server).get("/")
    assert response.status_code == 200
    csp = response.headers.get("Content-Security-Policy") or ""
    assert "style-src 'self'" in csp
    assert "fonts.googleapis.com" not in csp
    assert "fonts.gstatic.com" not in csp
    assert "'unsafe-inline'" not in csp


def test_spa_login_route_serves_index_html(web_server: ServerUnderTest, client_factory) -> None:
    response = client_factory(web_server).get("/login?session=expired", headers={"Accept": "text/html"})
    assert response.status_code == 200
    assert "text/html" in (response.headers.get("content-type") or "").lower()
    assert "Weir" in response.text


def test_missing_static_asset_still_returns_404(web_server: ServerUnderTest, client_factory) -> None:
    response = client_factory(web_server).get("/assets/missing.js", headers={"Accept": "*/*"})
    assert response.status_code == 404
