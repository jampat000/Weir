"""The Library view (#568): access, the Overview's totals and breakdowns, and the Problems grouping."""

from __future__ import annotations

import pytest

from tests.contract.libraries import _library_view_helpers as v


def test_the_library_view_endpoints_need_a_signed_in_user(server, client_factory, scanned) -> None:
    _admin, scanned_library = scanned
    # Built after `scanned`, which restarts the server on a new port.
    anonymous = client_factory(server)
    for suffix in ("library-overview", "library-problems", "library-files"):
        assert anonymous.get(f"{v.LIBRARIES}/{scanned_library}/{suffix}").status_code == 401


def test_a_library_that_does_not_exist_is_a_404(scanned) -> None:
    admin, scanned_library = scanned
    missing = scanned_library + 9_000
    for suffix in ("library-overview", "library-problems"):
        assert admin.get(f"{v.LIBRARIES}/{missing}/{suffix}").status_code == 404


def test_the_overview_totals_count_every_scanned_file_and_its_size(scanned) -> None:
    admin, scanned_library = scanned
    totals = v.overview(admin, scanned_library)["totals"]

    assert totals["files"] == 4
    assert totals["size_bytes"] == 6_500
    assert totals["matches"] == 1
    assert totals["would_change"] == 2
    assert totals["cannot_process"] == 1


def test_every_breakdown_is_returned_with_counts_and_shares(scanned) -> None:
    admin, scanned_library = scanned
    breakdowns = v.overview(admin, scanned_library)["breakdowns"]

    assert set(breakdowns) == {
        "video_codec",
        "resolution",
        "audio",
        "audio_language",
        "subtitle_language",
    }

    codecs = {row["value"]: row for row in breakdowns["video_codec"]}
    assert codecs["h264"]["files"] == 2
    assert codecs["hevc"]["files"] == 1
    assert codecs["unknown"]["files"] == 1
    assert codecs["h264"]["share"] == pytest.approx(0.5)
    # Biggest group first, so the bars read top-down.
    assert breakdowns["video_codec"][0]["value"] == "h264"

    languages = {row["value"]: row["files"] for row in breakdowns["audio_language"]}
    # A file with two English audio tracks still counts once.
    assert languages == {"eng": 3, "jpn": 1}

    audio = {row["value"]: row["files"] for row in breakdowns["audio"]}
    assert audio == {"ac3 5.1": 2, "aac stereo": 1, "eac3 5.1": 1}


def test_problems_are_grouped_by_reason_with_something_to_do_about_each(scanned) -> None:
    admin, scanned_library = scanned
    r = admin.get(f"{v.LIBRARIES}/{scanned_library}/library-problems")
    assert r.status_code == 200, r.text
    body = r.json()

    groups = {g["kind"]: g for g in body["groups"]}
    assert set(groups) == {"seeding", "unreadable"}
    assert body["total"] == 2
    assert groups["seeding"]["files"] == 1
    assert groups["seeding"]["sample_paths"] == ["/lib/seeding.mkv"]
    assert groups["seeding"]["what_to_do"]
    assert groups["unreadable"]["title"]
    # The same groups reach the Overview, so its "N files need a look" line agrees with this view.
    assert {g["kind"] for g in v.overview(admin, scanned_library)["problems"]} == set(groups)


def test_allowing_hardlinked_files_stops_seeding_being_reported_as_a_problem(scanned) -> None:
    admin, scanned_library = scanned
    settings = admin.get(f"{v.LIBRARIES}/{scanned_library}/library-settings").json()
    r = admin.put_csrf(
        f"{v.LIBRARIES}/{scanned_library}/library-settings",
        {
            "library_folders": settings["library_folders"],
            "clean_hardlinked_files": True,
        },
    )
    assert r.status_code == 200, r.text

    kinds = {g["kind"] for g in admin.get(f"{v.LIBRARIES}/{scanned_library}/library-problems").json()["groups"]}
    assert kinds == {"unreadable"}


def test_an_unscanned_library_answers_with_empty_totals(admin, server) -> None:
    r = admin.post_csrf(
        v.LIBRARIES,
        {
            "enabled": False,
            "name": "Never scanned",
            "media_type": "movie",
            "watched_folder": "/srv/never/in",
            "output_folder": "/srv/never/out",
        },
    )
    assert r.status_code == 201, r.text
    library_id = r.json()["id"]

    body = v.overview(admin, library_id)

    assert body["totals"]["files"] == 0
    assert body["folders_configured"] == 0
    assert body["scan"] is None
    assert body["breakdowns"]["video_codec"] == []
    assert body["problems"] == []
    assert v.files(admin, library_id)["files"] == []


def test_the_new_views_are_in_the_published_openapi_document(client) -> None:
    documented = client.get("/openapi.json").json()["paths"]

    for path in (
        "/api/v1/processing/libraries/{library_id}/library-overview",
        "/api/v1/processing/libraries/{library_id}/library-problems",
    ):
        assert path in documented, f"{path} is not in the served OpenAPI document"
        assert "get" in documented[path]
