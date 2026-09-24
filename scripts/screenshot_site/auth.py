"""Signing in without ever typing a password into the form.

Bootstraps a throwaway admin over the API, then drives the session the same way the app's own
login form does: ``/api/v1/auth/csrf``, then ``/api/v1/auth/login``, as a same-origin fetch with
credentials included. Both run from the page's own origin so the session cookie lands the normal
way. Split out of the main script (#747).
"""

from __future__ import annotations

from playwright.sync_api import Page

from .server_lifecycle import fail

_AUTH_FETCH_JS = """
async ({ path, payload }) => {
  const headers = { "Content-Type": "application/json", "X-Requested-With": "XMLHttpRequest" };
  const csrfRes = await fetch("/api/v1/auth/csrf", { credentials: "include", headers });
  const csrf = await csrfRes.json();
  const res = await fetch(path, {
    method: "POST",
    credentials: "include",
    headers,
    body: JSON.stringify({ ...payload, csrf_token: csrf.csrf_token }),
  });
  return { status: res.status, body: await res.text() };
}
"""


def api_bootstrap(page: Page, username: str, password: str) -> None:
    result = page.evaluate(
        _AUTH_FETCH_JS, {"path": "/api/v1/auth/bootstrap", "payload": {"username": username, "password": password}}
    )
    if result["status"] >= 400:
        fail(f"Bootstrapping the throwaway admin failed: HTTP {result['status']}: {result['body']}")


def api_login(page: Page, username: str, password: str) -> None:
    result = page.evaluate(
        _AUTH_FETCH_JS, {"path": "/api/v1/auth/login", "payload": {"username": username, "password": password}}
    )
    if result["status"] >= 400:
        fail(f"Signing in the throwaway admin failed: HTTP {result['status']}: {result['body']}")
