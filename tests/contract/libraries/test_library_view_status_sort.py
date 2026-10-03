"""The Library view's Files table sorted by status: where each file stands now, in the order the statuses read."""

from __future__ import annotations

import pytest

from tests.contract.libraries import _helpers as h
from tests.contract.libraries import _library_view_helpers as v
from tests.contract.support import seed

DIRECTIONS = ["asc", "desc"]
SMALL_PAGE = 2

# Matches, needs cleaning, cleaning, can't clean yet, can't clean yet because Weir cannot read or open the file, left alone.
# Files of one rank fall by path, whichever way the sort runs.
ASCENDING = [
    "/lib/also-ok.mkv",
    "/lib/show.mkv",
    "/lib/film.mkv",
    "/lib/cleaning.mkv",
    "/lib/no-video.mkv",
    "/lib/seeding.mkv",
    "/lib/broken.mkv",
    "/lib/no-permission.mkv",
    "/lib/left-alone.mkv",
]
DESCENDING = [
    "/lib/left-alone.mkv",
    "/lib/broken.mkv",
    "/lib/no-permission.mkv",
    "/lib/no-video.mkv",
    "/lib/seeding.mkv",
    "/lib/cleaning.mkv",
    "/lib/film.mkv",
    "/lib/also-ok.mkv",
    "/lib/show.mkv",
]
EXPECTED = {"asc": ASCENDING, "desc": DESCENDING}

STATUS_OF = {
    "/lib/also-ok.mkv": "matches",
    "/lib/show.mkv": "matches",
    "/lib/film.mkv": "needs_cleaning",
    "/lib/cleaning.mkv": "cleaning",
    "/lib/no-video.mkv": "cant_clean_yet",
    "/lib/seeding.mkv": "cant_clean_yet",
    "/lib/broken.mkv": "cant_clean_yet",
    "/lib/no-permission.mkv": "cant_clean_yet",
    "/lib/left-alone.mkv": "left_alone",
}


@pytest.fixture(scope="module", autouse=True)
def _library(server) -> int:
    """The four-file scan index of the Library view tests, plus one file in each status it leaves out and some that tie."""

    library_id = v.seed_scan_index(server)
    with seed.stopped(server) as conn:
        v.insert_library_file(
            conn, library_id=library_id, path="/lib/also-ok.mkv", classification="matches", probe_json=v.SHOW_PROBE
        )
        v.insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/cleaning.mkv",
            classification="would_change",
            probe_json=v.FILM_PROBE,
        )
        v.insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/no-permission.mkv",
            classification="cannot_process",
            probe_json="",
            problem_kind="no_permission",
        )
        v.insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/no-video.mkv",
            classification="cannot_process",
            probe_json="",
            problem_kind="no_video",
        )
        v.insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/left-alone.mkv",
            classification="would_change",
            probe_json=v.FILM_PROBE,
        )
        h.insert_job(
            conn,
            dedupe_key=f"processing.library.clean.v1:{library_id}:contract-cleaning",
            job_kind="processing.library.clean.v1",
            status="pending",
            payload={"path": "/lib/cleaning.mkv"},
        )
        conn.execute(
            "INSERT INTO library_file_marks (library_id, path, leave_alone) VALUES (?, '/lib/left-alone.mkv', 1)",
            (library_id,),
        )
    return library_id


@pytest.fixture
def listed(server, client_factory, _library):
    admin = h.signed_in_admin(server, client_factory)
    return admin, _library


def test_the_library_holds_a_file_in_every_status(listed) -> None:
    admin, library_id = listed

    body = v.files(admin, library_id)

    assert {f["path"]: f["status"] for f in body["files"]} == STATUS_OF
    assert {f["path"]: f["problem_kind"] for f in body["files"] if f["status"] == "cant_clean_yet"} == {
        "/lib/no-video.mkv": "no_video",
        "/lib/seeding.mkv": None,
        "/lib/broken.mkv": "unreadable",
        "/lib/no-permission.mkv": "no_permission",
    }


@pytest.mark.parametrize("direction", DIRECTIONS)
def test_files_sorted_by_status_follow_the_order_the_statuses_read(listed, direction: str) -> None:
    admin, library_id = listed

    body = v.files(admin, library_id, f"sort=status&direction={direction}")

    assert body["sort"] == "status"
    assert body["direction"] == direction
    assert v.paths(body) == EXPECTED[direction]


@pytest.mark.parametrize("direction", DIRECTIONS)
def test_paging_by_status_returns_every_file_once(listed, direction: str) -> None:
    admin, library_id = listed
    walked: list[str] = []
    pages = -(-len(STATUS_OF) // SMALL_PAGE)
    for page in range(1, pages + 1):
        walked += v.paths(
            v.files(admin, library_id, f"sort=status&direction={direction}&page_size={SMALL_PAGE}&page={page}")
        )

    assert walked == EXPECTED[direction]
    assert v.files(admin, library_id, f"sort=status&page_size={SMALL_PAGE}&page={pages + 1}")["files"] == []


def test_a_status_filter_and_the_status_sort_work_together(listed) -> None:
    admin, library_id = listed

    body = v.files(admin, library_id, "sort=status&status=cant_clean_yet")

    assert v.paths(body) == [
        "/lib/no-video.mkv",
        "/lib/seeding.mkv",
        "/lib/broken.mkv",
        "/lib/no-permission.mkv",
    ]


def test_a_sort_that_is_not_listed_still_falls_back_to_the_path(listed) -> None:
    admin, library_id = listed

    body = v.files(admin, library_id, "sort=statuses")

    assert body["sort"] == "path"
    assert v.paths(body) == sorted(STATUS_OF)
