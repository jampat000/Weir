"""Seeding the Library view's scan index for the "seeded" screenshot scenario: the scanned files,
their facets, the completed scan job, and what a clean would recover. Split out of
``seed_representative_data`` in the main script (#747); takes the caller's open connection so it
still runs inside the same transaction as the rest of the seed.
"""

from __future__ import annotations

import json
import secrets
import sqlite3
import time


def seed_library_files(conn: sqlite3.Connection) -> None:
    library_files = [
        (
            1,
            "Movies/Late Autumn Reprise (2023)/Late Autumn Reprise.mkv",
            6_100_000_000,
            "matches",
            "hevc",
            1080,
            "1080p",
            2,
            1,
            "eng DTS-HD 5.1, jpn AC3 2.0",
            "eng SDH",
            None,
        ),
        (
            1,
            "Movies/Harbor Static (2021)/Harbor Static.mkv",
            4_200_000_000,
            "would_change",
            "h264",
            1080,
            "1080p",
            3,
            4,
            "eng AC3 5.1, eng DTS 5.1, fra AC3 2.0",
            "eng, eng SDH, fra, spa",
            None,
        ),
        (
            1,
            "Movies/A Quiet Ledger (2022)/A Quiet Ledger.mkv",
            5_950_000_000,
            "cannot_process",
            "mpeg4",
            480,
            "sd",
            1,
            0,
            "eng AC3 2.0",
            None,
            "unreadable",
        ),
        (
            1,
            "Movies/Coldwater Run (2020)/Coldwater Run.mkv",
            3_600_000_000,
            "cannot_process",
            "h264",
            720,
            "720p",
            1,
            0,
            "eng AAC 2.0",
            None,
            "no_permission",
        ),
        (
            1,
            "Movies/The Long Shore (2019)/The Long Shore.mkv",
            12_500_000_000,
            "matches",
            "av1",
            2160,
            "4k",
            2,
            2,
            "eng TrueHD 7.1, deu AC3 5.1",
            "eng, deu",
            None,
        ),
        (
            1,
            "Movies/Nightfall Junction (2018)/Nightfall Junction.mkv",
            7_100_000_000,
            "would_change",
            "hevc",
            2160,
            "4k",
            4,
            6,
            "eng DTS-HD 7.1, eng AC3 2.0 (commentary), spa AC3 5.1, jpn AAC 2.0",
            "eng, eng SDH, spa, jpn, kor, por",
            None,
        ),
        (
            1,
            "Movies/Static Harbor Redux (2017)/Static Harbor Redux.mkv",
            5_400_000_000,
            "cannot_process",
            "h264",
            1080,
            "1080p",
            0,
            0,
            None,
            None,
            "no_audio_left",
        ),
        (
            1,
            "Movies/Rented Silence (2016)/Rented Silence.mkv",
            4_800_000_000,
            "cannot_process",
            "hevc",
            1080,
            "1080p",
            2,
            1,
            "eng DTS 5.1, fra AC3 2.0",
            "eng",
            "unreadable",
        ),
    ]
    for row in library_files:
        (
            library_id,
            path,
            size_bytes,
            classification,
            video_codec,
            video_height,
            resolution_class,
            audio_track_count,
            subtitle_track_count,
            audio_summary,
            subtitle_summary,
            problem_kind,
        ) = row
        cur = conn.execute(
            "INSERT INTO library_files (library_id, path, size_bytes, classification, video_codec, video_height, "
            "resolution_class, audio_track_count, subtitle_track_count, audio_summary, subtitle_summary, problem_kind) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            (
                library_id,
                path,
                size_bytes,
                classification,
                video_codec,
                video_height,
                resolution_class,
                audio_track_count,
                subtitle_track_count,
                audio_summary,
                subtitle_summary,
                problem_kind,
            ),
        )
        library_file_id = cur.lastrowid
        facets: list[tuple[str, str]] = [("video_codec", video_codec), ("resolution", resolution_class)]
        if audio_summary:
            facets.append(("audio", audio_summary.split(",")[0].strip()))
            for lang_token in audio_summary.split(","):
                lang = lang_token.strip().split(" ")[0]
                if lang:
                    facets.append(("audio_language", lang))
        if subtitle_summary:
            for lang_token in subtitle_summary.split(","):
                lang = lang_token.strip().split(" ")[0]
                if lang:
                    facets.append(("subtitle_language", lang))
        for facet, value in {(f, v) for f, v in facets if v}:
            conn.execute(
                "INSERT OR IGNORE INTO library_file_facets (library_id, library_file_id, facet, value) VALUES (?, ?, ?, ?)",
                (library_id, library_file_id, facet, value),
            )

    # Every "cannot be processed" row above carries one of the four reasons a scan itself can
    # reach (unreadable, no permission, no video, no audio left). "Still shared with a download"
    # and "the manager would download it again" are only ever found by a clean's preflight, on a
    # file the scan said would change, so seeding either on a cannot-process row is a state the
    # product cannot reach: it put a file in the tile and in no Problems group.
    #
    # The completed scan job is what the Library tab reads "Last scanned ..." from. Without it the
    # rows above look like a scan whose tracking job retention has since pruned, which is a real
    # state but not the one a README screenshot should show.
    scanned_at = int(time.time()) - 2 * 3600 - 17 * 60
    conn.execute(
        "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES (?, ?, ?, 'completed')",
        (
            f"processing.library.scan.v1:1:{secrets.token_hex(16)}",
            "processing.library.scan.v1",
            json.dumps({"library_id": 1, "ok": True, "scan_result": {"generated_at": scanned_at, "errors": []}}),
        ),
    )

    # What a clean would actually remove from the two "would change" files, and what that
    # would give back. The Library tab's Overview leads with these (they are the numbers a
    # clean acts on), so a seed that left them at their 0 defaults would show the tab's
    # headline figure as an empty one.
    conn.executemany(
        "UPDATE library_files SET removed_audio_tracks = ?, removed_subtitle_tracks = ?, "
        "estimated_bytes_saved = ? WHERE library_id = 1 AND path = ?",
        [
            (1, 2, 310_000_000, "Movies/Harbor Static (2021)/Harbor Static.mkv"),
            (2, 4, 940_000_000, "Movies/Nightfall Junction (2018)/Nightfall Junction.mkv"),
        ],
    )
