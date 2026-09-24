"""Shared building blocks for the hand-off status, lifecycle and folder contract tests.

Split out of one file (#747) so each test file stays under the project's line-count guideline;
behaviour is unchanged, only the file a name lives in.
"""

from __future__ import annotations

import uuid
from pathlib import Path
from typing import Any

import httpx

from tests.contract.media_managers._helpers import CALLBACK_PATH, NO_WEBHOOK_SECRET, handoff_dedupe_key
from tests.contract.support import seed
from tests.contract.support.client import API
from tests.contract.support.launcher import ServerUnderTest

SECRET_VALUE = "s3cret"
SECRET = {"X-Webhook-Secret": SECRET_VALUE}
SECRET_ENV = {**NO_WEBHOOK_SECRET, "WEIR_MEDIA_MANAGER_WEBHOOK_SECRET": SECRET_VALUE}
#: Hand-off answers are kept this long once finished (the server's ledger retention).
LEDGER_RETENTION_DAYS = 90


def _new_id() -> str:
    return f"h-{uuid.uuid4().hex[:12]}"


def _hand_off_response(server: ServerUnderTest, handoff_id: str, source_path: Path) -> httpx.Response:
    return httpx.post(
        f"{server.base_url}{API}/intake/webhook/deluno",
        headers=SECRET,
        json={
            "eventType": "deluno.processor-handoff",
            "handoffId": handoff_id,
            "libraryId": "lib-1",
            "mediaType": "movies",
            "sourcePath": str(source_path),
            "callbackPath": CALLBACK_PATH,
        },
        timeout=30,
    )


def _hand_off(server: ServerUnderTest, handoff_id: str, source_path: Path) -> None:
    response = _hand_off_response(server, handoff_id, source_path)
    assert response.status_code == 200, response.text


def _status_response(server: ServerUnderTest, handoff_id: str, headers: dict[str, str] | None = None) -> httpx.Response:
    return httpx.get(
        f"{server.base_url}{API}/intake/handoffs/deluno/{handoff_id}",
        headers=SECRET if headers is None else headers,
        timeout=30,
    )


def _status(server: ServerUnderTest, handoff_id: str) -> dict[str, Any]:
    response = _status_response(server, handoff_id)
    assert response.status_code == 200, response.text
    return response.json()


def _cancel(server: ServerUnderTest, handoff_id: str) -> httpx.Response:
    return httpx.delete(f"{server.base_url}{API}/intake/handoffs/deluno/{handoff_id}", headers=SECRET, timeout=30)


def _seed(
    server: ServerUnderTest,
    handoff_id: str,
    *,
    job_status: str | None = None,
    file_status: str | None = None,
    relative_path: str | None = None,
    **file_fields: Any,
) -> None:
    """While the server is stopped: move the hand-off's job rows, and give its file a Files row.

    ``leased`` cannot be seeded this way: startup treats a leased row as a dead worker's and
    requeues it. The tests that need running work use a real worker instead.
    """

    with seed.stopped(server) as conn:
        key = handoff_dedupe_key(handoff_id)
        if job_status is not None:
            changed = conn.execute(
                "UPDATE jobs SET status = ? WHERE dedupe_key = ? OR dedupe_key LIKE ?",
                (job_status, key, f"{key}:%"),
            ).rowcount
            assert changed, f"no job rows for {handoff_id}"
        if file_status is not None:
            ledger = seed.rows(
                conn,
                "SELECT library_id, relative_path FROM media_manager_handoffs WHERE source_key = ? AND handoff_id = ?",
                ("deluno", handoff_id),
            )
            assert ledger, f"no ledger row for {handoff_id}"
            columns = {
                "library_id": ledger[0]["library_id"],
                "relative_path": relative_path or ledger[0]["relative_path"],
                "status": file_status,
                **file_fields,
            }
            # An upsert: a server may already hold a Files row for a handed-over file that exists on disk
            # (the .NET server records its size on receipt, #531).
            updates = ", ".join(
                f"{name} = excluded.{name}" for name in columns if name not in ("library_id", "relative_path")
            )
            conn.execute(
                f"INSERT INTO files ({', '.join(columns)}) VALUES ({', '.join('?' for _ in columns)}) "
                f"ON CONFLICT (library_id, relative_path) DO UPDATE SET {updates}",
                tuple(columns.values()),
            )
