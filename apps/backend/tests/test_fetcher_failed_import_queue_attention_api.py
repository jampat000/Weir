"""Authenticated ``GET /api/v1/fetcher/failed-imports/queue-attention-snapshot``."""

from __future__ import annotations

from starlette.testclient import TestClient

from tests.integration_helpers import auth_post, csrf as fetch_csrf


def _login(client: TestClient) -> None:
    tok = fetch_csrf(client)
    r = auth_post(
        client,
        "/api/v1/auth/login",
        json={"username": "alice", "password": "test-password-strong", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text


def test_queue_attention_snapshot_ok_shape(client_with_admin: TestClient) -> None:
    _login(client_with_admin)
    r = client_with_admin.get("/api/v1/fetcher/failed-imports/queue-attention-snapshot")
    assert r.status_code == 200, r.text
    body = r.json()
    assert "tv_shows" in body and "movies" in body
    assert "needs_attention_count" in body["tv_shows"]
    assert "last_checked_at" in body["tv_shows"]
