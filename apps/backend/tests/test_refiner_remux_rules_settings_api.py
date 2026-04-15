"""GET/PUT ``/api/v1/refiner/remux-rules-settings``."""

from __future__ import annotations

from starlette.testclient import TestClient

from tests.integration_helpers import auth_post, csrf as fetch_csrf, trusted_browser_origin_headers


def _login_admin(client: TestClient) -> None:
    tok = fetch_csrf(client)
    r = auth_post(
        client,
        "/api/v1/auth/login",
        json={"username": "alice", "password": "test-password-strong", "csrf_token": tok},
    )
    assert r.status_code == 200, r.text


def test_refiner_remux_rules_settings_get_shape(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    r = client_with_admin.get("/api/v1/refiner/remux-rules-settings")
    assert r.status_code == 200, r.text
    body = r.json()
    assert body["primary_audio_lang"] == "eng"
    assert body["subtitle_mode"] in ("remove_all", "keep_selected")
    assert "audio_preference_mode" in body
    assert "updated_at" in body


def test_refiner_remux_rules_settings_put_updates(client_with_admin: TestClient) -> None:
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = client_with_admin.put(
        "/api/v1/refiner/remux-rules-settings",
        json={
            "csrf_token": tok,
            "primary_audio_lang": "fre",
            "secondary_audio_lang": "eng",
            "tertiary_audio_lang": "",
            "default_audio_slot": "secondary",
            "remove_commentary": False,
            "subtitle_mode": "keep_selected",
            "subtitle_langs_csv": "eng,fre",
            "preserve_forced_subs": True,
            "preserve_default_subs": False,
            "audio_preference_mode": "quality_all_languages",
        },
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r.status_code == 200, r.text
    out = r.json()
    assert out["primary_audio_lang"] == "fre"
    assert out["subtitle_mode"] == "keep_selected"
    assert out["subtitle_langs_csv"] == "eng,fre"
    assert out["audio_preference_mode"] == "quality_all_languages"


def test_refiner_remux_rules_settings_put_rejects_keep_selected_without_langs(
    client_with_admin: TestClient,
) -> None:
    _login_admin(client_with_admin)
    tok = fetch_csrf(client_with_admin)
    r = client_with_admin.put(
        "/api/v1/refiner/remux-rules-settings",
        json={
            "csrf_token": tok,
            "subtitle_mode": "keep_selected",
            "subtitle_langs_csv": "   ",
        },
        headers={**trusted_browser_origin_headers(), "Content-Type": "application/json"},
    )
    assert r.status_code == 400, r.text
