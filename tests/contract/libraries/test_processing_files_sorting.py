"""The Files list in each order it can be asked for: sorted by path, status or time, in either direction, paged by cursor."""

from __future__ import annotations

from datetime import UTC, datetime
from typing import Any

import pytest

from tests.contract.libraries import _helpers as h
from tests.contract.support import seed
from tests.contract.support.client import API

FILES = f"{API}/processing/files"

SORTS = ["file", "status", "when"]
DIRECTIONS = ["asc", "desc"]
# What a request that names no sort gets, spelled out as the parameters that give the same order.
ORDERS = [(sort, direction) for sort in [*SORTS, None] for direction in DIRECTIONS]

FILE_COUNT = 36
SMALL_PAGE = 5
WHOLE_LIST = 1000

STATUSES = [
    "unprocessed",
    "processing",
    "processed",
    "processing_failed",
    "skipped",
    "disabled",
    "on_hold",
    "out_of_schedule",
    "blocked_upstream",
    "passed_through",
    "rejected",
    "cancelled",
]

# What each status means, in the order a list sorted by status shows them: done, to do, doing, needs a look, broken, idle.
MEANING_RANK = {
    "processed": 0,
    "unprocessed": 1,
    "out_of_schedule": 1,
    "processing": 2,
    "on_hold": 3,
    "blocked_upstream": 1,
    "passed_through": 3,
    "rejected": 3,
    "processing_failed": 4,
    "skipped": 5,
    "disabled": 5,
    "cancelled": 5,
}
STATUSES_BY_MEANING = [
    "processed",
    "blocked_upstream",
    "out_of_schedule",
    "unprocessed",
    "processing",
    "on_hold",
    "passed_through",
    "rejected",
    "processing_failed",
    "cancelled",
    "disabled",
    "skipped",
]

# Before every time a scan could have recorded.
NEVER = datetime.min.replace(tzinfo=UTC)

CHANGED_AT = ["2026-09-01 08:00:00", "2026-09-01 09:30:00", "2026-09-02 10:00:00", "2026-09-03 11:15:00"]
SEEN_AT = ["2026-10-01 06:00:00", "2026-10-01 07:00:00", "2026-10-02 08:00:00"]


@pytest.fixture(scope="module", autouse=True)
def _seeded(server) -> None:
    """
    Files that tie on every sort: pairs of paths that differ only in case, a handful of change times shared by many
    files, every status, and some files no scan has seen.
    """

    with seed.stopped(server) as conn:
        library_id = h.first_library_id(conn)
        for index in range(FILE_COUNT):
            path = f"Title {index // 2:02d}/Episode.mkv"
            h.insert_file(
                conn,
                library_id=library_id,
                relative_path=path.lower() if index % 2 else path,
                status=STATUSES[index % len(STATUSES)],
                updated_at=CHANGED_AT[index % len(CHANGED_AT)],
                last_seen_at=None if index % 5 == 0 else SEEN_AT[index % len(SEEN_AT)],
            )


def _get(client, **params: Any) -> dict[str, Any]:
    r = client.get(FILES, params={"limit": WHOLE_LIST, **params})
    assert r.status_code == 200, r.text
    return r.json()


def _ids(body: dict[str, Any]) -> list[int]:
    return [row["id"] for row in body["files"]]


def _query(sort: str | None, direction: str) -> dict[str, str]:
    return {"direction": direction} if sort is None else {"sort": sort, "direction": direction}


def _time(text: str | None) -> datetime | None:
    if text is None:
        return None
    moment = datetime.fromisoformat(text.replace("Z", "+00:00"))
    return moment if moment.tzinfo else moment.replace(tzinfo=UTC)


def _expected(rows: list[dict[str, Any]], sort: str | None, direction: str) -> list[int]:
    """The files as the order should read, worked out from what each row shows rather than from the server's rules."""

    key = {
        "file": lambda row: (row["relative_path"].lower(), row["id"]),
        "status": lambda row: (MEANING_RANK[row["status"]], row["status"], row["id"]),
        "when": lambda row: (_time(row["updated_at"]), row["id"]),
        # A file no scan has seen sorts before every file one has seen.
        None: lambda row: (row["last_seen_at"] is not None, _time(row["last_seen_at"]) or NEVER, row["id"]),
    }[sort]
    return [row["id"] for row in sorted(rows, key=key, reverse=direction == "desc")]


def test_the_seeded_files_tie_on_every_sort(admin) -> None:
    rows = _get(admin)["files"]

    assert len(rows) == FILE_COUNT
    assert {row["status"] for row in rows} == set(STATUSES)
    assert len({row["relative_path"].lower() for row in rows}) == FILE_COUNT // 2
    assert len({row["updated_at"] for row in rows}) < FILE_COUNT / 2
    assert any(row["last_seen_at"] is None for row in rows)


@pytest.mark.parametrize(("sort", "direction"), ORDERS)
def test_files_come_in_the_order_the_sort_and_direction_ask_for(admin, sort: str | None, direction: str) -> None:
    body = _get(admin, **_query(sort, direction))

    assert _ids(body) == _expected(body["files"], sort, direction)
    assert body["next_cursor"] is None


def test_a_list_asked_for_no_sort_is_newest_seen_first_and_files_never_seen_come_last(admin) -> None:
    rows = _get(admin)["files"]

    seen = [_time(row["last_seen_at"]) for row in rows]
    first_unseen = seen.index(None)
    assert all(moment is None for moment in seen[first_unseen:])
    assert seen[:first_unseen] == sorted(seen[:first_unseen], reverse=True)


def test_sorting_by_status_follows_what_the_status_means_and_then_the_status_word(admin) -> None:
    rows = _get(admin, sort="status", direction="asc")["files"]

    distinct: list[str] = []
    for row in rows:
        if row["status"] not in distinct:
            distinct.append(row["status"])
    assert distinct == STATUSES_BY_MEANING
    assert [row["status"] for row in _get(admin, sort="status", direction="desc")["files"]] == [
        row["status"] for row in reversed(rows)
    ]


@pytest.mark.parametrize("sort", ["file", "status", "when"])
def test_files_that_tie_on_the_sort_value_fall_by_id_in_the_direction_of_the_sort(admin, sort: str) -> None:
    for direction in DIRECTIONS:
        rows = _get(admin, sort=sort, direction=direction)["files"]
        ties: dict[Any, list[int]] = {}
        for row in rows:
            value = {"file": row["relative_path"].lower(), "status": row["status"], "when": row["updated_at"]}[sort]
            ties.setdefault(value, []).append(row["id"])

        assert any(len(ids) > 1 for ids in ties.values())
        assert all(ids == sorted(ids, reverse=direction == "desc") for ids in ties.values())


@pytest.mark.parametrize(("sort", "direction"), ORDERS)
def test_paging_in_any_order_visits_every_file_once_and_adds_up_to_the_whole_list(
    admin, sort: str | None, direction: str
) -> None:
    whole = _get(admin, **_query(sort, direction))
    walked: list[int] = []
    cursor: str | None = None
    pages = 0
    for _ in range(FILE_COUNT):
        page = _get(admin, **_query(sort, direction), limit=SMALL_PAGE, **({"cursor": cursor} if cursor else {}))
        walked += _ids(page)
        assert page["limit"] == SMALL_PAGE
        assert page["returned"] == len(page["files"])
        assert page["status_counts"] == whole["status_counts"]
        cursor = page["next_cursor"]
        pages += 1
        if cursor is None:
            break

    assert cursor is None
    assert pages == -(-FILE_COUNT // SMALL_PAGE)
    assert walked == _ids(whole)
    assert len(set(walked)) == FILE_COUNT


@pytest.mark.parametrize("sort", SORTS)
def test_a_page_that_ends_exactly_on_the_last_file_has_no_cursor(admin, sort: str) -> None:
    exact = _get(admin, sort=sort, limit=FILE_COUNT)
    one_short = _get(admin, sort=sort, limit=FILE_COUNT - 1)
    last = _get(admin, sort=sort, limit=FILE_COUNT - 1, cursor=one_short["next_cursor"])

    assert exact["next_cursor"] is None
    assert one_short["next_cursor"] is not None
    assert last["returned"] == 1
    assert last["next_cursor"] is None
    assert [*_ids(one_short), *_ids(last)] == _ids(exact)


@pytest.mark.parametrize("sort", SORTS)
def test_a_filter_and_a_sort_page_together(admin, sort: str) -> None:
    filters = {"file_status": "processing_failed,rejected,processed", "path_contains": "title 0"}
    whole = _ids(_get(admin, sort=sort, direction="asc", **filters))
    walked: list[int] = []
    cursor: str | None = None
    for _ in range(FILE_COUNT):
        page = _get(admin, sort=sort, direction="asc", limit=2, **filters, **({"cursor": cursor} if cursor else {}))
        walked += _ids(page)
        cursor = page["next_cursor"]
        if cursor is None:
            break

    assert walked == whole
    assert 2 < len(whole) < FILE_COUNT


@pytest.mark.parametrize(
    "params",
    [
        {"sort": "name"},
        {"sort": "last_seen"},
        {"sort": "File"},
        {"sort": ""},
        {"direction": "up"},
        {"direction": "ASC"},
        {"direction": ""},
        {"sort": "file", "direction": "sideways"},
    ],
)
def test_a_sort_or_direction_the_list_does_not_have_is_refused(admin, params: dict[str, Any]) -> None:
    r = admin.get(FILES, params=params)

    assert r.status_code == 422, r.text
    assert r.json()["detail"][0]["loc"][0] == "query"


@pytest.mark.parametrize(
    "other",
    [
        {"sort": "file", "direction": "desc"},
        {"sort": "status", "direction": "asc"},
        {"sort": "when", "direction": "asc"},
        {"direction": "asc"},
        {},
    ],
)
def test_a_cursor_made_for_another_sort_or_direction_is_refused(admin, other: dict[str, Any]) -> None:
    cursor = _get(admin, sort="file", direction="asc", limit=SMALL_PAGE)["next_cursor"]

    r = admin.get(FILES, params={**other, "cursor": cursor})

    assert r.status_code == 422, r.text
    assert r.json()["detail"][0]["loc"] == ["query", "cursor"]


@pytest.mark.parametrize("cursor", ["nonsense", "e30", "W10", "eyJzb3J0IjoiZmlsZSJ9", "AAAA", "!!"])
def test_a_cursor_the_list_did_not_give_out_is_refused_under_every_sort(admin, cursor: str) -> None:
    for sort in [*SORTS, None]:
        r = admin.get(FILES, params={**_query(sort, "asc"), "cursor": cursor})

        assert r.status_code == 422, (sort, r.text)
