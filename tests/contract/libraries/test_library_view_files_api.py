"""The Library view's Files table (#568): per-file media facts, facet filters, sorting and paging."""

from __future__ import annotations

import pytest

from tests.contract.libraries import _library_view_helpers as v


def test_a_file_reports_the_media_facts_the_table_shows(scanned) -> None:
    admin, scanned_library = scanned
    film = next(f for f in v.files(admin, scanned_library)["files"] if f["path"] == "/lib/film.mkv")

    assert film["video_codec"] == "hevc"
    assert film["resolution_class"] == "4k"
    assert film["video_height"] == 2160
    assert film["audio_track_count"] == 2
    assert film["audio_summary"] == "eng eac3 5.1, jpn aac stereo"
    assert film["subtitle_summary"] == "eng"
    assert film["manager_title"] == "Blade Runner 2049"


def test_a_file_with_no_cached_probe_reads_unknown(scanned) -> None:
    admin, scanned_library = scanned
    broken = next(f for f in v.files(admin, scanned_library)["files"] if f["path"] == "/lib/broken.mkv")

    assert broken["video_codec"] == "unknown"
    assert broken["resolution_class"] == "unknown"
    assert broken["audio_summary"] is None
    assert broken["problem_kind"] == "unreadable"


@pytest.mark.parametrize(
    ("query", "expected"),
    [
        ("audio_language=jpn", ["/lib/film.mkv"]),
        ("resolution=1080p", ["/lib/seeding.mkv", "/lib/show.mkv"]),
        ("video_codec=h264&resolution=1080p", ["/lib/seeding.mkv", "/lib/show.mkv"]),
        ("video_codec=hevc&resolution=1080p", []),
        ("subtitle_language=eng", ["/lib/film.mkv"]),
        ("audio=ac3 5.1", ["/lib/seeding.mkv", "/lib/show.mkv"]),
        ("classification=would_change", ["/lib/film.mkv", "/lib/seeding.mkv"]),
        ("problem=seeding", ["/lib/seeding.mkv"]),
        ("problem=unreadable", ["/lib/broken.mkv"]),
        ("manager=radarr", ["/lib/film.mkv"]),
        ("q=runner", ["/lib/film.mkv"]),
        ("q=seed", ["/lib/seeding.mkv"]),
        # An unknown facet name is ignored rather than narrowing (or reaching) the query.
        ("not_a_facet=hevc", ["/lib/broken.mkv", "/lib/film.mkv", "/lib/seeding.mkv", "/lib/show.mkv"]),
    ],
)
def test_a_facet_filter_narrows_the_files_listing(scanned, query, expected) -> None:
    admin, scanned_library = scanned
    assert v.paths(v.files(admin, scanned_library, query)) == expected


def test_a_filter_narrows_the_filtered_totals_but_not_the_header_summary(scanned) -> None:
    admin, scanned_library = scanned
    body = v.files(admin, scanned_library, "audio_language=jpn")

    assert body["total"] == 1
    assert body["filtered"]["files"] == 1
    assert body["filtered"]["size_bytes"] == 3_000
    assert body["summary"]["files"] == 4
    assert body["summary"]["size_bytes"] == 6_500


def test_files_are_sorted_by_the_named_column_in_both_directions(scanned) -> None:
    admin, scanned_library = scanned
    ascending = v.files(admin, scanned_library, "sort=size&direction=asc")
    descending = v.files(admin, scanned_library, "sort=size&direction=desc")

    assert ascending["sort"] == "size"
    assert ascending["direction"] == "asc"
    assert v.paths(ascending) == [
        "/lib/broken.mkv",
        "/lib/show.mkv",
        "/lib/seeding.mkv",
        "/lib/film.mkv",
    ]
    assert v.paths(descending) == list(reversed(v.paths(ascending)))


def test_an_unknown_sort_falls_back_to_the_path_rather_than_failing(scanned) -> None:
    admin, scanned_library = scanned
    body = v.files(admin, scanned_library, "sort=size_bytes;DROP+TABLE+library_files")

    assert body["sort"] == "path"
    assert v.paths(body) == ["/lib/broken.mkv", "/lib/film.mkv", "/lib/seeding.mkv", "/lib/show.mkv"]
    # Proof the rejected text never reached the database: the rows are all still there.
    assert body["summary"]["files"] == 4


def test_paging_returns_every_row_exactly_once(scanned) -> None:
    admin, scanned_library = scanned
    first = v.files(admin, scanned_library, "page_size=3")
    second = v.files(admin, scanned_library, "page_size=3&page=2")

    assert first["page"] == 1
    assert first["page_size"] == 3
    assert first["total"] == 4
    assert len(first["files"]) == 3
    assert v.paths(second) == ["/lib/show.mkv"]
    assert sorted(v.paths(first) + v.paths(second)) == [
        "/lib/broken.mkv",
        "/lib/film.mkv",
        "/lib/seeding.mkv",
        "/lib/show.mkv",
    ]


def test_a_page_size_beyond_the_cap_is_clamped(scanned) -> None:
    admin, scanned_library = scanned
    body = v.files(admin, scanned_library, "page_size=100000&page=0")

    assert body["page_size"] == 200
    assert body["page"] == 1
