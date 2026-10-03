"""System › Logs in each order it can be listed in: sorted by any column, in either direction, paged by cursor."""

from __future__ import annotations

import csv
import io
from collections import Counter
from datetime import datetime
from typing import Any

import pytest

from tests.contract.support import seed
from tests.contract.support.client import API
from tests.contract.system import _helpers as h
from tests.contract.system import _log_seed as seeded

LOG = f"{API}/system/log"
EXPORT = f"{API}/system/log/export"

SORTS = ["time", "level", "source", "category", "workflow"]
DIRECTIONS = ["asc", "desc"]
EVERY_ORDER = [(sort, direction) for sort in SORTS for direction in DIRECTIONS]

ROWS_PER_SOURCE = 12
ROWS = ROWS_PER_SOURCE * 3
SMALL_PAGE = 5
WHOLE_LOG = 100

# Names that differ by case, so ignoring case is visible: "Bravo" comes before "alpha" only when case counts.
WORKFLOWS = ["alpha", "Bravo", "charlie"]

LEVEL_RANK = {"error": 0, "warning": 1, "info": 2, "success": 3}
SOURCE_RANK = {"server": 0, "job": 1, "event": 2}

EVENT_TYPES = ["auth.login_succeeded", "library.scan_completed", "library.watched_folder_added"]
EVENT_RESULTS = ["failed", "success", "warning", "running"]
JOB_KINDS = ["processing.file.remux_pass.v1", "processing.work_temp_stale_sweep.v1", "processing.library.clean.v1"]
JOB_STATUSES = [("failed", "ffmpeg stopped"), ("pending", None), ("completed", None), ("cancelled", None)]
SERVER_LOGGERS = ["weir.processing", "weir.platform.suite_settings.backups", "weir.library_mode.router"]
SERVER_LEVELS = ["ERROR", "WARNING", "INFO"]


@pytest.fixture(scope="module", autouse=True)
def _seeded(server) -> None:
    """Twelve events, jobs and server lines that share levels, categories, workflows and instants, so every sort has ties."""

    h.seed_users(server)
    logs = server.home / "logs"
    logs.mkdir(parents=True, exist_ok=True)
    with seed.stopped(server) as conn:
        conn.execute("DELETE FROM jobs")
        workflows = []
        for name in WORKFLOWS:
            conn.execute("INSERT INTO libraries (name, media_type) VALUES (?, 'movie')", (name,))
            workflows.append(int(seed.scalar(conn, "SELECT id FROM libraries WHERE name = ?", (name,))))
        lines = []
        for index in range(ROWS_PER_SOURCE):
            moment = seeded.at(10 + index // 4)
            workflow = None if index % 4 == 3 else workflows[index % 3]
            seeded.event(
                conn,
                moment,
                EVENT_TYPES[index % 3],
                f"Event {index}",
                result=EVENT_RESULTS[index % 4],
                library_id=workflow,
            )
            status, last_error = JOB_STATUSES[index % 4]
            seeded.job(
                conn,
                moment,
                f"contract:sort:{index}",
                JOB_KINDS[index % 3],
                status,
                last_error=last_error,
                library_id=workflow,
            )
            lines.append(
                seeded.log_line(moment, SERVER_LEVELS[index % 3], SERVER_LOGGERS[(index // 3) % 3], f"Line {index}")
            )
        # Written while the server is stopped, since it holds its log open while it runs.
        with (logs / "weir.log").open("a", encoding="utf-8") as log:
            log.write("\n".join(lines) + "\n")


def _get(client, **params: Any) -> dict[str, Any]:
    r = client.get(LOG, params={**seeded.WINDOW, **params})
    assert r.status_code == 200, r.text
    return r.json()


def _ids(body: dict[str, Any]) -> list[str]:
    return [item["id"] for item in body["items"]]


def _when(item: dict[str, Any]) -> datetime:
    return datetime.fromisoformat(item["at"].replace("Z", "+00:00"))


def _place(item: dict[str, Any]) -> tuple[datetime, int, int]:
    """When it happened, its source and its number in the source: what every order ends in, and no two rows share."""

    source, number = item["id"].split(":")
    return _when(item), SOURCE_RANK[source], int(number)


def _expected(items: list[dict[str, Any]], sort: str, direction: str) -> list[str]:
    """The rows as the order should read, worked out from what each row shows rather than from the server's rules."""

    descending = direction == "desc"

    def ordered(rows: list[dict[str, Any]], leading) -> list[dict[str, Any]]:
        return sorted(rows, key=lambda item: (leading(item), _place(item)), reverse=descending)

    if sort == "workflow":
        named = [item for item in items if item["workflow"] and item["workflow"]["name"]]
        unnamed = [item for item in items if item not in named]
        return [
            item["id"]
            for item in ordered(named, lambda item: item["workflow"]["name"].lower()) + ordered(unnamed, lambda item: 0)
        ]
    leading = {
        "time": lambda item: 0,
        "level": lambda item: LEVEL_RANK[item["level"]],
        "source": lambda item: item["source"],
        "category": lambda item: item["category"],
    }[sort]
    return [item["id"] for item in ordered(items, leading)]


def test_the_seeded_log_has_rows_that_tie_on_every_sort(admin) -> None:
    items = _get(admin, limit=WHOLE_LOG)["items"]

    assert Counter(item["source"] for item in items) == {
        "event": ROWS_PER_SOURCE,
        "job": ROWS_PER_SOURCE,
        "server": ROWS_PER_SOURCE,
    }
    assert {item["level"] for item in items} == set(LEVEL_RANK)
    assert len({item["category"] for item in items}) >= 4
    assert {item["workflow"]["name"] for item in items if item["workflow"]} == set(WORKFLOWS)
    assert any(item["workflow"] is None for item in items)
    assert len({_when(item) for item in items}) < ROWS


@pytest.mark.parametrize(("sort", "direction"), EVERY_ORDER)
def test_rows_come_in_the_order_the_sort_and_direction_ask_for(admin, sort: str, direction: str) -> None:
    items = _get(admin, limit=WHOLE_LOG, sort=sort, direction=direction)["items"]

    assert [item["id"] for item in items] == _expected(items, sort, direction)


def test_the_list_is_newest_first_when_it_is_not_asked_to_sort(admin) -> None:
    plain = _get(admin, limit=WHOLE_LOG)
    explicit = _get(admin, limit=WHOLE_LOG, sort="time", direction="desc")
    oldest_first = _get(admin, limit=WHOLE_LOG, direction="asc")

    assert _ids(plain) == _ids(explicit)
    assert _ids(oldest_first) == list(reversed(_ids(plain)))


def test_levels_run_from_errors_down_to_successes_when_ascending(admin) -> None:
    items = _get(admin, limit=WHOLE_LOG, sort="level", direction="asc")["items"]

    ranks = [LEVEL_RANK[item["level"]] for item in items]
    assert ranks == sorted(ranks)
    assert ranks[0] == 0
    assert ranks[-1] == LEVEL_RANK["success"]


@pytest.mark.parametrize("direction", DIRECTIONS)
def test_rows_that_tie_on_the_sort_value_fall_by_time_in_the_direction_of_the_sort(admin, direction: str) -> None:
    items = _get(admin, limit=WHOLE_LOG, sort="level", direction=direction)["items"]

    for level in LEVEL_RANK:
        times = [_when(item) for item in items if item["level"] == level]
        assert times == sorted(times, reverse=direction == "desc"), level


@pytest.mark.parametrize("direction", DIRECTIONS)
def test_rows_with_no_workflow_come_last_whichever_way_the_workflow_sort_runs(admin, direction: str) -> None:
    items = _get(admin, limit=WHOLE_LOG, sort="workflow", direction=direction)["items"]

    named = [bool(item["workflow"]) for item in items]
    assert named == sorted(named, reverse=True)
    assert any(named) and not all(named)
    names = [item["workflow"]["name"].lower() for item in items if item["workflow"]]
    assert names == sorted(names, reverse=direction == "desc")


@pytest.mark.parametrize(("sort", "direction"), EVERY_ORDER)
def test_paging_in_any_order_visits_every_row_once_and_adds_up_to_the_whole_list(
    admin, sort: str, direction: str
) -> None:
    whole = _ids(_get(admin, limit=WHOLE_LOG, sort=sort, direction=direction))
    walked: list[str] = []
    cursor: str | None = None
    for _ in range(ROWS):
        page = _get(admin, limit=SMALL_PAGE, sort=sort, direction=direction, **({"cursor": cursor} if cursor else {}))
        walked += _ids(page)
        assert page["total"] == ROWS
        cursor = page["next_cursor"]
        if cursor is None:
            break

    assert cursor is None
    assert walked == whole
    assert len(set(walked)) == ROWS


@pytest.mark.parametrize("sort", SORTS)
def test_a_filter_and_a_sort_page_together(admin, sort: str) -> None:
    filters = {"source": "event,job", "level": "error,warning"}
    whole = _ids(_get(admin, limit=WHOLE_LOG, sort=sort, direction="asc", **filters))
    walked: list[str] = []
    cursor: str | None = None
    for _ in range(ROWS):
        page = _get(admin, limit=3, sort=sort, direction="asc", **filters, **({"cursor": cursor} if cursor else {}))
        walked += _ids(page)
        cursor = page["next_cursor"]
        if cursor is None:
            break

    assert walked == whole
    assert 0 < len(whole) < ROWS


@pytest.mark.parametrize(
    "params",
    [
        {"sort": "message"},
        {"sort": "Level"},
        {"sort": ""},
        {"direction": "sideways"},
        {"direction": "ASC"},
        {"direction": ""},
        {"sort": "level", "direction": "up"},
    ],
)
def test_a_sort_or_direction_the_log_does_not_have_is_refused(admin, params: dict[str, Any]) -> None:
    for url in (LOG, EXPORT):
        r = admin.get(url, params={**seeded.WINDOW, **params})

        assert r.status_code == 422, r.text
        assert r.json()["detail"]
        assert r.json()["detail"][0]["loc"][0] == "query"


@pytest.mark.parametrize(
    "other",
    [
        {"sort": "level", "direction": "desc"},
        {"sort": "category", "direction": "asc"},
        {"sort": "time", "direction": "asc"},
        {"sort": "time", "direction": "desc"},
        {"direction": "asc"},
        {},
    ],
)
def test_a_cursor_made_for_another_sort_or_direction_is_refused(admin, other: dict[str, Any]) -> None:
    cursor = _get(admin, limit=SMALL_PAGE, sort="level", direction="asc")["next_cursor"]

    r = admin.get(LOG, params={**seeded.WINDOW, **other, "cursor": cursor})

    assert r.status_code == 422, r.text
    assert r.json()["detail"][0]["loc"] == ["query", "cursor"]


@pytest.mark.parametrize("cursor", ["nonsense", "e30", "W10", "eyJzb3J0IjoibGV2ZWwifQ", "AAAA", "!!"])
def test_a_cursor_the_log_did_not_give_out_is_refused_under_every_sort(admin, cursor: str) -> None:
    for sort in SORTS:
        r = admin.get(LOG, params={**seeded.WINDOW, "sort": sort, "cursor": cursor})

        assert r.status_code == 422, (sort, r.text)


@pytest.mark.parametrize(("sort", "direction"), EVERY_ORDER)
def test_the_export_lists_the_rows_in_the_order_the_screen_shows_them(admin, sort: str, direction: str) -> None:
    on_screen = _ids(_get(admin, limit=WHOLE_LOG, sort=sort, direction=direction))

    as_json = admin.get(EXPORT, params={**seeded.WINDOW, "format": "json", "sort": sort, "direction": direction})
    as_csv = admin.get(EXPORT, params={**seeded.WINDOW, "format": "csv", "sort": sort, "direction": direction})

    assert as_json.status_code == 200, as_json.text
    assert [row["id"] for row in as_json.json()] == on_screen
    assert as_csv.status_code == 200, as_csv.text
    titles = [row["title"] for row in csv.DictReader(io.StringIO(as_csv.text))]
    by_title = {item["id"]: item["title"] for item in _get(admin, limit=WHOLE_LOG)["items"]}
    assert titles == [by_title[row_id] for row_id in on_screen]
