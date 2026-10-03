"""Rows for the System › Logs contract tests, written straight into the database and the server log."""

from __future__ import annotations

import json
from datetime import UTC, datetime, timedelta
from typing import Any

from tests.contract.support import seed

# Signing in and starting the server write rows of their own at the real time; everything seeded here is two days
# back, and each request is held to that day so it sees only what the test put there.
SEEDED_AT = (datetime.now(UTC) - timedelta(days=2)).replace(microsecond=0)
WINDOW = {"from": (SEEDED_AT - timedelta(hours=1)).isoformat(), "to": (SEEDED_AT + timedelta(hours=1)).isoformat()}


def at(minutes: int) -> datetime:
    return SEEDED_AT + timedelta(minutes=minutes)


def log_line(at: datetime, level: str, logger: str, message: str, **extra: Any) -> str:
    entry = {
        "timestamp": at.strftime("%Y-%m-%dT%H:%M:%S.%f") + "Z",
        "level": level,
        "logger": logger,
        "message": message,
        "source": None,
        "detail": None,
        "correlation_id": None,
        "job_id": None,
        **extra,
    }
    return json.dumps(entry)


def event(
    conn,
    at: datetime,
    event_type: str,
    title: str,
    *,
    result: str,
    trigger: str | None = None,
    library_id: int | None = None,
    relative_path: str | None = None,
) -> None:
    conn.execute(
        'INSERT INTO activity_events (created_at, event_type, module, title, result, "trigger", library_id, relative_path) '
        "VALUES (?, ?, 'processing', ?, ?, ?, ?, ?)",
        (seed.utc_text(at), event_type, title, result, trigger, library_id, relative_path),
    )


def job(
    conn,
    at: datetime,
    key: str,
    kind: str,
    status: str,
    *,
    last_error: str | None = None,
    library_id: int | None = None,
):
    payload = json.dumps({"library_id": library_id}) if library_id is not None else None
    conn.execute(
        "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, attempt_count, max_attempts, last_error, "
        "created_at, updated_at) VALUES (?, ?, ?, ?, 1, 3, ?, ?, ?)",
        (key, kind, payload, status, last_error, seed.utc_text(at), seed.utc_text(at)),
    )
    return int(conn.execute("SELECT last_insert_rowid()").fetchone()[0])
