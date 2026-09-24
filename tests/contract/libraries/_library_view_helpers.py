"""Seeding and reading the Library view's scan index (#568), shared by the Library view contract tests.

The scan index is seeded straight into ``library_files`` (with the derived columns and facet rows a scan
writes) while the server is stopped, because what is under test is the reading of that index. Running a
real scan needs real media files and is covered by the server's own test suite.
"""

from __future__ import annotations

import json
from typing import Any

from tests.contract.libraries import _helpers as h
from tests.contract.support import seed
from tests.contract.support.client import API, WeirClient
from tests.contract.support.launcher import ServerUnderTest

LIBRARIES = f"{API}/processing/libraries"

FILM_PROBE = json.dumps(
    {
        "streams": [
            {"codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160},
            {
                "codec_type": "audio",
                "codec_name": "eac3",
                "channels": 6,
                "channel_layout": "5.1(side)",
                "tags": {"language": "eng"},
            },
            {"codec_type": "audio", "codec_name": "aac", "channels": 2, "tags": {"language": "jpn"}},
            {"codec_type": "subtitle", "codec_name": "subrip", "tags": {"language": "eng"}},
        ]
    }
)

SHOW_PROBE = json.dumps(
    {
        "streams": [
            {"codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080},
            {"codec_type": "audio", "codec_name": "ac3", "channels": 6, "tags": {"language": "eng"}},
        ]
    }
)


#: One seeded row: the columns a scan derives from the probe JSON, and the facet rows it writes with them.
#: Kept in the test rather than computed, so a change to the server's own derivation shows up here as a
#: failure instead of being mirrored automatically.
FACTS: dict[str, dict[str, Any]] = {
    FILM_PROBE: {
        "video_codec": "hevc",
        "video_height": 2160,
        "resolution_class": "4k",
        "audio_track_count": 2,
        "subtitle_track_count": 1,
        "audio_summary": "eng eac3 5.1, jpn aac stereo",
        "subtitle_summary": "eng",
        "facets": [
            ("video_codec", "hevc"),
            ("resolution", "4k"),
            ("audio", "eac3 5.1"),
            ("audio", "aac stereo"),
            ("audio_language", "eng"),
            ("audio_language", "jpn"),
            ("subtitle_language", "eng"),
        ],
    },
    SHOW_PROBE: {
        "video_codec": "h264",
        "video_height": 1080,
        "resolution_class": "1080p",
        "audio_track_count": 1,
        "subtitle_track_count": 0,
        "audio_summary": "eng ac3 5.1",
        "subtitle_summary": None,
        "facets": [
            ("video_codec", "h264"),
            ("resolution", "1080p"),
            ("audio", "ac3 5.1"),
            ("audio_language", "eng"),
        ],
    },
    "": {
        "video_codec": "unknown",
        "video_height": None,
        "resolution_class": "unknown",
        "audio_track_count": 0,
        "subtitle_track_count": 0,
        "audio_summary": None,
        "subtitle_summary": None,
        "facets": [("video_codec", "unknown"), ("resolution", "unknown")],
    },
}


def insert_library_file(
    conn: Any,
    *,
    library_id: int,
    path: str,
    classification: str,
    probe_json: str,
    size_bytes: int = 1_000_000,
    link_count: int | None = None,
    problem_kind: str | None = None,
    manager_kind: str | None = None,
    manager_title: str | None = None,
) -> None:
    facts = FACTS[probe_json]
    cur = conn.execute(
        "INSERT INTO library_files (library_id, path, size_bytes, mtime, classification, "
        "removed_audio_tracks, removed_subtitle_tracks, estimated_bytes_saved, video_codec, "
        "video_height, resolution_class, audio_track_count, subtitle_track_count, audio_summary, "
        "subtitle_summary, link_count, problem_kind, manager_kind, manager_title) "
        "VALUES (?, ?, ?, 1700000000, ?, 0, 0, 0, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
        (
            library_id,
            path,
            size_bytes,
            classification,
            facts["video_codec"],
            facts["video_height"],
            facts["resolution_class"],
            facts["audio_track_count"],
            facts["subtitle_track_count"],
            facts["audio_summary"],
            facts["subtitle_summary"],
            link_count,
            problem_kind,
            manager_kind,
            manager_title,
        ),
    )
    file_id = int(cur.lastrowid or 0)
    conn.execute("INSERT INTO library_file_probes (library_file_id, probe_json) VALUES (?, ?)", (file_id, probe_json))
    for facet, value in facts["facets"]:
        conn.execute(
            "INSERT OR IGNORE INTO library_file_facets (library_id, library_file_id, facet, value) VALUES (?, ?, ?, ?)",
            (library_id, file_id, facet, value),
        )


def seed_scan_index(server: ServerUnderTest) -> int:
    """Replace the first library's scan index with four deliberately varied files; return its id.

    ``seed.stopped`` restarts the server on a new port, so make clients only after this returns.
    """

    with seed.stopped(server) as conn:
        library_id = h.first_library_id(conn)
        conn.execute("DELETE FROM library_files WHERE library_id = ?", (library_id,))
        insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/film.mkv",
            classification="would_change",
            probe_json=FILM_PROBE,
            size_bytes=3_000,
            manager_kind="radarr",
            manager_title="Blade Runner 2049",
        )
        insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/show.mkv",
            classification="matches",
            probe_json=SHOW_PROBE,
            size_bytes=1_000,
        )
        insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/seeding.mkv",
            classification="would_change",
            probe_json=SHOW_PROBE,
            size_bytes=2_000,
            link_count=2,
        )
        insert_library_file(
            conn,
            library_id=library_id,
            path="/lib/broken.mkv",
            classification="cannot_process",
            probe_json="",
            size_bytes=500,
            problem_kind="unreadable",
        )
    return library_id


def overview(c: WeirClient, library_id: int) -> dict[str, Any]:
    r = c.get(f"{LIBRARIES}/{library_id}/library-overview")
    assert r.status_code == 200, r.text
    return r.json()


def files(c: WeirClient, library_id: int, query: str = "") -> dict[str, Any]:
    r = c.get(f"{LIBRARIES}/{library_id}/library-files" + (f"?{query}" if query else ""))
    assert r.status_code == 200, r.text
    return r.json()


def paths(body: dict[str, Any]) -> list[str]:
    return [f["path"] for f in body["files"]]
