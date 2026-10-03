"""System › Logs exported: every row the filters leave, up to the announced limit, in the order the list shows."""

from __future__ import annotations

import csv
import io
from datetime import timedelta
from typing import Any

import pytest

from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.system import _helpers as h
from tests.contract.system import _log_seed as seeded

LOG = f"{API}/system/log"
EXPORT = f"{API}/system/log/export"

# More events than one internal read of the export holds, so the export has to carry on from where a read stopped.
EVENTS = 1100
JOBS = 60
SERVER_LINES = 60
ROWS = EVENTS + JOBS + SERVER_LINES
LIST_PAGE = 100
EXPORT_LIMIT = "50000"

ORDERS = [("time", "desc"), ("time", "asc"), ("level", "asc")]


@pytest.fixture(scope="module", autouse=True)
def _seeded(server) -> None:
    h.seed_users(server)
    logs = server.home / "logs"
    logs.mkdir(parents=True, exist_ok=True)
    with seed.stopped(server) as conn:
        conn.execute("DELETE FROM jobs")
        for index in range(EVENTS):
            moment = seeded.SEEDED_AT + timedelta(seconds=index)
            result = "failed" if index % 7 == 0 else "success"
            seeded.event(conn, moment, "library.scan_completed", f"Event {index}", result=result)
        for index in range(JOBS):
            moment = seeded.SEEDED_AT + timedelta(seconds=index)
            seeded.job(conn, moment, f"contract:export:{index}", "processing.file.remux_pass.v1", "pending")
        lines = [
            seeded.log_line(seeded.SEEDED_AT + timedelta(seconds=index), "INFO", "weir.processing", f"Line {index}")
            for index in range(SERVER_LINES)
        ]
        # Written while the server is stopped, since it holds its log open while it runs.
        with (logs / "weir.log").open("a", encoding="utf-8") as log:
            log.write("\n".join(lines) + "\n")


def _walk_list(client: WeirClient, **params: Any) -> list[str]:
    """Every row id the interactive list gives, a page of its own cap at a time."""

    ids: list[str] = []
    cursor: str | None = None
    for _ in range(ROWS):
        page = client.get(
            LOG, params={**seeded.WINDOW, **params, "limit": LIST_PAGE, **({"cursor": cursor} if cursor else {})}
        )
        assert page.status_code == 200, page.text
        body = page.json()
        ids += [item["id"] for item in body["items"]]
        cursor = body["next_cursor"]
        if cursor is None:
            break
    return ids


def _export_json(client: WeirClient, **params: Any) -> list[dict[str, Any]]:
    r = client.get(EXPORT, params={**seeded.WINDOW, **params, "format": "json"})
    assert r.status_code == 200, r.text
    return r.json()


def test_the_list_holds_a_hundred_rows_a_page_however_many_the_log_has(admin) -> None:
    page = admin.get(LOG, params={**seeded.WINDOW, "limit": LIST_PAGE}).json()

    assert page["total"] == ROWS
    assert len(page["items"]) == LIST_PAGE
    assert page["next_cursor"] is not None
    assert admin.get(LOG, params={**seeded.WINDOW, "limit": LIST_PAGE + 1}).status_code == 422


@pytest.mark.parametrize(("sort", "direction"), ORDERS)
def test_the_json_export_holds_every_row_in_the_order_the_list_walks_them(admin, sort: str, direction: str) -> None:
    rows = _export_json(admin, sort=sort, direction=direction)

    assert [row["id"] for row in rows] == _walk_list(admin, sort=sort, direction=direction)
    assert len(rows) == ROWS


def test_the_csv_export_holds_every_row_and_says_how_many(admin) -> None:
    r = admin.get(EXPORT, params={**seeded.WINDOW, "format": "csv"})

    assert r.status_code == 200, r.text
    rows = list(csv.DictReader(io.StringIO(r.text)))
    assert len(rows) == ROWS
    assert r.headers["x-weir-export-rows"] == str(ROWS)
    assert r.headers["x-weir-export-limit"] == EXPORT_LIMIT
    assert {row["source"] for row in rows} == {"event", "job", "server"}


def test_the_csv_and_json_exports_hold_the_same_rows(admin) -> None:
    as_csv = list(csv.DictReader(io.StringIO(admin.get(EXPORT, params={**seeded.WINDOW, "format": "csv"}).text)))
    as_json = _export_json(admin)

    assert [row["title"] for row in as_csv] == [row["title"] for row in as_json]


def test_an_export_holds_every_row_the_filters_leave_and_no_others(admin) -> None:
    events = _export_json(admin, source="event")
    failed = _export_json(admin, source="event", level="error")

    assert len(events) == EVENTS
    assert {row["source"] for row in events} == {"event"}
    assert len(failed) == len(range(0, EVENTS, 7))
    assert {row["level"] for row in failed} == {"error"}
