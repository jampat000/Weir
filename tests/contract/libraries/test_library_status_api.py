"""The Library view's status (#568 follow-up): where each file stands now against the current rules.

Exactly one status per file, so the counts add up to the files. Left alone beats cleaning, which beats
cant_clean_yet, which beats needs_cleaning, which beats matches. The scan index the ``scanned`` fixture seeds holds one
file the rules would change, one that matches, one still shared with a download and one Weir cannot read.
"""

from __future__ import annotations

import pytest

from tests.contract.libraries import _library_view_helpers as v

STATUSES = ["needs_cleaning", "cleaning", "matches", "cant_clean_yet", "left_alone"]


def _status_by_path(body: dict) -> dict[str, str]:
    return {f["path"]: f["status"] for f in body["files"]}


def test_every_file_has_one_status(scanned) -> None:
    admin, library_id = scanned

    assert _status_by_path(v.files(admin, library_id)) == {
        "/lib/film.mkv": "needs_cleaning",
        "/lib/show.mkv": "matches",
        "/lib/seeding.mkv": "cant_clean_yet",
        "/lib/broken.mkv": "cant_clean_yet",
    }


def test_the_counts_by_status_add_up_to_the_files(scanned) -> None:
    admin, library_id = scanned
    totals = v.overview(admin, library_id)["totals"]

    assert totals["by_status"] == {
        "needs_cleaning": 1,
        "cleaning": 0,
        "matches": 1,
        "cant_clean_yet": 2,
        "left_alone": 0,
    }
    assert sum(totals["by_status"].values()) == totals["files"]


def test_the_files_listing_carries_the_same_counts_for_the_header_and_for_the_filter(scanned) -> None:
    admin, library_id = scanned
    body = v.files(admin, library_id, "status=cant_clean_yet")

    assert sum(body["summary"]["by_status"].values()) == body["summary"]["files"] == 4
    assert body["filtered"]["by_status"]["cant_clean_yet"] == 2
    assert body["filtered"]["by_status"]["matches"] == 0


@pytest.mark.parametrize(
    ("status", "expected"),
    [
        ("needs_cleaning", ["/lib/film.mkv"]),
        ("matches", ["/lib/show.mkv"]),
        ("cant_clean_yet", ["/lib/broken.mkv", "/lib/seeding.mkv"]),
        ("cleaning", []),
        ("left_alone", []),
        # A status the server does not know narrows nothing.
        ("nonsense", ["/lib/broken.mkv", "/lib/film.mkv", "/lib/seeding.mkv", "/lib/show.mkv"]),
    ],
)
def test_the_files_listing_narrows_to_one_status(scanned, status, expected) -> None:
    admin, library_id = scanned

    assert v.paths(v.files(admin, library_id, f"status={status}")) == expected


def test_a_file_set_aside_is_left_alone_whatever_the_rules_say(scanned) -> None:
    admin, library_id = scanned
    base = f"{v.LIBRARIES}/{library_id}/library-files/leave-alone"

    r = admin.post_csrf(base, json={"path": "/lib/film.mkv", "leave_alone": True})
    assert r.status_code == 200, r.text

    body = v.files(admin, library_id)
    assert _status_by_path(body)["/lib/film.mkv"] == "left_alone"
    assert v.overview(admin, library_id)["totals"]["by_status"]["left_alone"] == 1
    assert v.paths(v.files(admin, library_id, "status=left_alone")) == ["/lib/film.mkv"]


def test_a_reason_is_only_given_where_the_scan_can_support_one(scanned) -> None:
    admin, library_id = scanned
    reasons = {f["path"]: f["status_reason"] for f in v.files(admin, library_id)["files"]}

    # The seeded index was written without a reason, so none is invented for the file that needs cleaning.
    assert reasons["/lib/film.mkv"] is None
    assert all(reason is None for reason in reasons.values())


def test_the_status_names_are_the_documented_ones(scanned) -> None:
    admin, library_id = scanned

    assert {f["status"] for f in v.files(admin, library_id)["files"]} <= set(STATUSES)
