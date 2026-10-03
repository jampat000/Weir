"""What the live Connections views are told: usage on each connection, and a frame on the Activity stream per call."""

from __future__ import annotations

from typing import Any

import pytest

from tests.contract.media_managers._helpers import (
    NO_WEBHOOK_SECRET,
    clear_connections,
    create_connection,
    create_connection_with_secret,
)
from tests.contract.support.client import API, WeirClient
from tests.contract.support.fake_manager import FakeManager, Reply
from tests.contract.support.sse import SseReader, open_stream

HEALTH_PATH = "/api/integrations/external/health"
FRAME_FIELDS = {"kind", "id", "phase", "direction", "at", "ms"}


@pytest.fixture(scope="module")
def server_env() -> dict[str, str]:
    return dict(NO_WEBHOOK_SECRET)


@pytest.fixture
def operator(admin: WeirClient) -> WeirClient:
    """A signed-in operator with no media manager connections left over."""

    clear_connections(admin)
    return admin


def _connection_frames(stream: SseReader, connection_id: int, count: int) -> list[dict[str, Any]]:
    """The next ``count`` ``connection.activity`` frames that are about one media manager connection."""

    frames: list[dict[str, Any]] = []
    while len(frames) < count:
        frame = stream.next_event_named("connection.activity")
        if frame["kind"] == "media_manager" and frame["id"] == connection_id:
            frames.append(frame)
    return frames


def _opened(stream: SseReader) -> None:
    """Read the frames every stream opens with, so it is certainly listening when the test acts."""

    assert stream.next_block() == ["retry: 5000"]
    stream.next_event_named("activity.latest")


def _add_manager(operator: WeirClient, fake: FakeManager) -> int:
    created = create_connection(operator, base_url=fake.base_url, api_key=fake.api_key)
    assert created.status_code == 201, created.text
    return created.json()["id"]


def test_a_manager_never_used_reports_no_answer_time_and_no_last_use(operator: WeirClient) -> None:
    created = create_connection(operator)

    assert created.status_code == 201, created.text
    row = created.json()
    assert row["last_answer_ms"] is None
    assert row["last_used_at"] is None


def test_a_download_client_never_used_reports_no_answer_time_and_no_last_use(operator: WeirClient) -> None:
    created = operator.post_csrf(
        f"{API}/download-clients/connections", {"kind": "qbittorrent", "base_url": "http://192.0.2.30:8080"}
    )

    assert created.status_code == 201, created.text
    row = created.json()
    assert row["last_answer_ms"] is None
    assert row["last_used_at"] is None


def test_testing_a_manager_records_how_long_it_took_and_when(operator: WeirClient, fake_managers) -> None:
    fake = fake_managers("deluno")
    connection_id = _add_manager(operator, fake)

    tested = operator.post_csrf(f"{API}/media-managers/connections/{connection_id}/test")

    assert tested.status_code == 200 and tested.json()["ok"] is True, tested.text
    row = operator.get(f"{API}/media-managers/connections/{connection_id}").json()
    assert isinstance(row["last_answer_ms"], int) and row["last_answer_ms"] >= 0
    assert row["last_used_at"].endswith("Z")
    listed = operator.get(f"{API}/media-managers/connections").json()
    assert [(item["last_answer_ms"], item["last_used_at"]) for item in listed] == [
        (row["last_answer_ms"], row["last_used_at"])
    ]


def test_testing_a_manager_sends_an_asked_frame_and_then_an_answered_one(
    server, operator: WeirClient, fake_managers
) -> None:
    fake = fake_managers("deluno")
    connection_id = _add_manager(operator, fake)

    with open_stream(server, operator) as stream:
        _opened(stream)
        assert operator.post_csrf(f"{API}/media-managers/connections/{connection_id}/test").status_code == 200
        asked, answered = _connection_frames(stream, connection_id, 2)

    assert set(asked) == FRAME_FIELDS
    assert (asked["phase"], asked["direction"], asked["ms"]) == ("asked", "outbound", None)
    assert (answered["phase"], answered["direction"]) == ("answered", "outbound")
    assert isinstance(answered["ms"], int) and answered["ms"] >= 0
    assert answered["at"].endswith("Z")


def test_a_manager_that_answers_with_an_error_sends_a_failed_frame(server, operator: WeirClient, fake_managers) -> None:
    fake = fake_managers("deluno")
    fake.route("GET", HEALTH_PATH, Reply(500))
    connection_id = _add_manager(operator, fake)

    with open_stream(server, operator) as stream:
        _opened(stream)
        assert operator.post_csrf(f"{API}/media-managers/connections/{connection_id}/test").status_code == 200
        _, ended = _connection_frames(stream, connection_id, 2)

    assert (ended["phase"], ended["direction"]) == ("failed", "outbound")
    assert isinstance(ended["ms"], int)


def test_a_manager_calling_weir_with_its_secret_sends_an_inbound_answered_frame(server, operator: WeirClient) -> None:
    row, headers = create_connection_with_secret(operator, kind="radarr", base_url="http://192.0.2.20:7878")

    with open_stream(server, operator) as stream:
        _opened(stream)
        posted = operator.post(
            f"{API}/intake/webhook/radarr", json={"eventType": "Grab", "movie": {"id": 1}}, headers=headers
        )
        assert posted.status_code == 200, posted.text
        (frame,) = _connection_frames(stream, row["id"], 1)

    assert set(frame) == FRAME_FIELDS
    assert (frame["phase"], frame["direction"], frame["ms"]) == ("answered", "inbound", None)
    assert frame["kind"] == "media_manager"
    used = operator.get(f"{API}/media-managers/connections/{row['id']}").json()["last_used_at"]
    assert used is not None and used.endswith("Z")
