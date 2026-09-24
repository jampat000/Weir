"""Seeding the libraries, files and jobs tables for the "seeded" screenshot scenario.

Plain SQL against the server's own SQLite file — no fixtures added to the product. Split out of
``seed_representative_data`` in the main script (#747); each function takes the caller's open
connection so every seed function still runs inside one transaction.
"""

from __future__ import annotations

import sqlite3


def seed_libraries(conn: sqlite3.Connection) -> None:
    conn.execute(
        "UPDATE libraries SET watched_folder = ?, work_folder = ?, output_folder = ?, "
        "schedule_hours_limited = 1, schedule_start = '01:00', schedule_end = '06:00' "
        "WHERE id = 1",
        (r"D:\Media\Incoming\Movies", r"D:\Media\Work\Movies", r"D:\Media\Library\Movies"),
    )
    conn.execute(
        "UPDATE libraries SET watched_folder = ?, work_folder = ?, output_folder = ? WHERE id = 2",
        (r"D:\Media\Incoming\TV", r"D:\Media\Work\TV", r"D:\Media\Library\TV"),
    )


def seed_files(conn: sqlite3.Connection) -> None:
    files = [
        (
            1,
            "Movies/Arrival of the Kestrel (2024)/Arrival of the Kestrel.mkv",
            "unprocessed",
            "",
            8_400_000_000,
        ),
        (
            1,
            "Movies/Arrival of the Kestrel (2024)/Arrival of the Kestrel.remux.mkv",
            "processing",
            "",
            8_400_000_000,
        ),
        (1, "Movies/Late Autumn Reprise (2023)/Late Autumn Reprise.mkv", "processed", "", 6_100_000_000),
        (
            1,
            "Movies/A Quiet Ledger (2022)/A Quiet Ledger.mkv",
            "processing_failed",
            "ffmpeg exited with a video stream error",
            5_950_000_000,
        ),
        (
            1,
            "Movies/Harbor Static (2021)/Harbor Static.mkv",
            "on_hold",
            "waiting for the file to stop growing",
            4_200_000_000,
        ),
        (2, "TV/Northline/Season 01/Northline.S01E01.mkv", "unprocessed", "", 1_800_000_000),
        (2, "TV/Northline/Season 01/Northline.S01E02.mkv", "processed", "", 1_750_000_000),
        (
            2,
            "TV/Northline/Season 01/Northline.S01E03.mkv",
            "passed_through",
            "already matches every rule",
            1_820_000_000,
        ),
        (
            2,
            "TV/Coastal Drift/Season 02/Coastal Drift.S02E04.mkv",
            "blocked_upstream",
            "Sonarr has not confirmed the import yet",
            1_600_000_000,
        ),
    ]
    conn.executemany(
        "INSERT INTO files (library_id, relative_path, status, status_reason, size_bytes) VALUES (?, ?, ?, ?, ?)",
        files,
    )


def seed_jobs(conn: sqlite3.Connection) -> None:
    jobs = [
        (
            "screenshot-seed-remux-1",
            "processing.file.remux_pass.v1",
            '{"relative_media_path": "Movies/Arrival of the Kestrel (2024)/Arrival of the Kestrel.mkv", "media_scope": "movie"}',
            "leased",
        ),
        (
            "screenshot-seed-remux-2",
            "processing.file.remux_pass.v1",
            '{"relative_media_path": "Movies/Late Autumn Reprise (2023)/Late Autumn Reprise.mkv", "media_scope": "movie"}',
            "completed",
        ),
        (
            "screenshot-seed-remux-3",
            "processing.file.remux_pass.v1",
            '{"relative_media_path": "Movies/A Quiet Ledger (2022)/A Quiet Ledger.mkv", "media_scope": "movie"}',
            "failed",
        ),
        (
            "screenshot-seed-scan-1",
            "processing.watched_folder.remux_scan_dispatch.v1",
            '{"library_id": 2}',
            "pending",
        ),
        (
            "screenshot-seed-sweep-1",
            "processing.work_temp_stale_sweep.v1",
            "{}",
            "completed",
        ),
    ]
    conn.executemany(
        "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, last_error) VALUES (?, ?, ?, ?, ?)",
        [(*row, "ffmpeg exited with a video stream error" if row[3] == "failed" else None) for row in jobs],
    )
