"""The activity freshness stream (server-sent events) and how recording an event treats retention."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

from tests.contract.activity._helpers import insert_event
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.sse import STREAM_TIMEOUT, open_stream


def _latest_activity_id(client: WeirClient) -> int:
    r = client.get(f"{API}/activity/recent", params={"limit": 100})
    assert r.status_code == 200, r.text
    return max(int(item["id"]) for item in r.json()["items"])


def test_activity_stream_requires_authentication(client: WeirClient) -> None:
    r = client.get(f"{API}/activity/stream", timeout=STREAM_TIMEOUT)
    assert r.status_code == 401


def test_activity_stream_authenticated_emits_latest_format(server, admin: WeirClient) -> None:
    with open_stream(server, admin) as stream:
        assert stream.response.status_code == 200
        assert stream.response.headers["content-type"].startswith("text/event-stream")
        assert "no-store" in stream.response.headers["cache-control"]
        assert stream.response.headers["x-accel-buffering"] == "no"

        assert stream.next_block() == ["retry: 5000"]
        event, data = stream.next_event()
        assert event == "activity.latest"
        assert set(data) == {"latest_event_id", "activity_revision"}
        assert data["latest_event_id"] == _latest_activity_id(admin)
        assert isinstance(data["activity_revision"], int)


def test_activity_stream_emits_a_newer_id_and_revision_after_a_new_event(
    server, admin: WeirClient, client_factory
) -> None:
    with open_stream(server, admin) as stream:
        _event, first = stream.next_event()
        # Signing in again records an Activity event while the stream is open. The stream holds no
        # database session, so the write is not blocked by it.
        other = client_factory(server)
        other.login()
        latest = _latest_activity_id(admin)
        assert latest > first["latest_event_id"]

        event, data = stream.next_event()
        while data["latest_event_id"] < latest:
            event, data = stream.next_event()
        assert event == "activity.latest"
        assert data["latest_event_id"] == latest
        assert data["activity_revision"] > first["activity_revision"]


def test_record_activity_event_does_not_prune_history_using_log_retention(server_factory, client_factory) -> None:
    sut = server_factory()
    client = client_factory(sut)
    client.ensure_admin()
    current = client.get(f"{API}/suite/settings").json()
    saved = client.put_csrf(
        f"{API}/suite/settings",
        json={
            "app_timezone": current["app_timezone"],
            "log_retention_days": 1,
            "activity_retention_days": current["activity_retention_days"],
        },
    )
    assert saved.status_code == 200, saved.text

    title = "Old Processing result that still backs overview history"
    with seed.stopped(sut) as conn:
        insert_event(
            conn,
            event_type="processing.file_remux_pass_completed",
            module="processing",
            title=title,
            detail="{}",
            created_at=datetime.now(UTC) - timedelta(days=10),
            result="success",
        )
    client = client_factory(sut)
    client.login()  # records a new sign-in event

    r = client.get(f"{API}/activity/recent", params={"limit": 100})
    assert r.status_code == 200, r.text
    titles = [item["title"] for item in r.json()["items"]]
    assert title in titles
    assert any(item["module"] == "auth" for item in r.json()["items"])
