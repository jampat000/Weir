"""Seeding Activity events, a notification channel and a media manager connection for the
"seeded" screenshot scenario. Split out of ``seed_representative_data`` in the main script
(#747); the seed functions take the caller's open connection so they still run inside the same
transaction as the rest of the seed.
"""

from __future__ import annotations

import json
import sqlite3


def remux_pass_detail(
    *,
    outcome: str,
    trigger: str,
    result: str,
    user_message: str,
    relative_path: str,
    source_size_bytes: int,
    output_size_bytes: int,
    audio_removed: int,
    subtitles_removed: int,
    media_scope: str = "movie",
    copied_without_remux: bool = False,
) -> str:
    """One finished pass's Activity detail, shaped like RemuxPassVisibility.ClipForActivity's output.

    The envelope keys (module/action/trigger/result/severity/counts/user_message) come from
    Observability.ActivityDetailEnvelope; the rest are the pass payload keys Processing -> Overview
    reads back out of it.
    """

    detail = {
        "module": "processing",
        "action": "remux",
        "trigger": trigger,
        "result": result,
        "severity": "info" if result == "success" else "error",
        "media_scope": media_scope,
        "media_scope_label": "Movie" if media_scope == "movie" else "TV",
        "counts": {"audio_removed": audio_removed, "subtitles_removed": subtitles_removed},
        "user_message": user_message,
        "ok": True,
        "outcome": outcome,
        "relative_media_path": relative_path,
        "source_size_bytes": source_size_bytes,
        "output_size_bytes": output_size_bytes,
    }
    if copied_without_remux:
        detail["output_copied_without_remux"] = True
    return json.dumps(detail)


def seed_activity_events(conn: sqlite3.Connection) -> None:
    # Processing -> Overview's "last 30 days" figures are not read off these rows' text: they
    # re-parse the detail of `processing.file_remux_pass_completed` events as the JSON envelope
    # RemuxPassHandler writes (see OverviewStatsStore.BuildAsync). A finished pass seeded as a
    # plain "job_completed" line therefore shows up in the feed and nowhere in the figures, which
    # is how a seeded install ended up reporting 0 files handed back and a 0% success rate beside
    # two files it had just handed back. So the two finished passes below carry the real event
    # type and the real envelope.
    activity = [
        (
            "processing.file_remux_pass_completed",
            "processing",
            "Late Autumn Reprise finished",
            remux_pass_detail(
                outcome="live_output_written",
                trigger="schedule",
                result="success",
                user_message="Late Autumn Reprise.mkv was processed successfully",
                relative_path="Movies/Late Autumn Reprise (2023)/Late Autumn Reprise.mkv",
                source_size_bytes=6_100_000_000,
                output_size_bytes=5_870_000_000,
                audio_removed=1,
                subtitles_removed=0,
            ),
            "schedule",
            "ok",
        ),
        (
            "job_failed",
            "processing",
            "A Quiet Ledger could not be processed",
            "ffmpeg exited with a video stream error",
            "schedule",
            "failed",
        ),
        (
            "library_scan",
            "library",
            "Movies library scan finished",
            "8 files scanned, 2 would change, 4 cannot be processed",
            "manual",
            "ok",
        ),
        (
            "processing.file_remux_pass_completed",
            "processing",
            "Northline S01E02 finished",
            remux_pass_detail(
                outcome="live_skipped_not_required",
                trigger="watched_folder",
                result="success",
                user_message="No changes needed for Northline.S01E02.mkv",
                relative_path="TV/Northline/Season 01/Northline.S01E02.mkv",
                source_size_bytes=1_750_000_000,
                output_size_bytes=1_750_000_000,
                audio_removed=0,
                subtitles_removed=0,
                media_scope="tv",
                copied_without_remux=True,
            ),
            "watched_folder",
            "ok",
        ),
        (
            "connection_test",
            "media_manager",
            "Sonarr connection tested",
            "Reachable, 214 series",
            "manual",
            "ok",
        ),
        (
            "job_failed",
            "processing",
            "Coastal Drift S02E04 held",
            "Sonarr has not confirmed the import yet",
            "watched_folder",
            "warning",
        ),
        (
            "configuration_backup",
            "suite",
            "Configuration backup completed",
            "12 tables, 480 KB",
            "schedule",
            "ok",
        ),
        (
            "maintenance",
            "suite",
            "Stale work-file sweep completed",
            "Removed 2 abandoned temp files",
            "schedule",
            "ok",
        ),
    ]
    conn.executemany(
        'INSERT INTO activity_events (event_type, module, title, detail, "trigger", result) VALUES (?, ?, ?, ?, ?, ?)',
        activity,
    )


def seed_notifications_and_connections(conn: sqlite3.Connection) -> None:
    conn.execute(
        "INSERT INTO notification_channels (label, provider, url, events_json, enabled) VALUES (?, ?, ?, ?, ?)",
        ("Ops webhook", "generic", "https://example.invalid/hooks/weir", '["job_failed", "job_completed"]', 1),
    )
    # A passing test is only a result with a time behind it: the product records both in
    # one write, and Settings > Media managers will not say "Connected" without the time.
    conn.execute(
        "INSERT INTO media_manager_connections (kind, name, enabled, base_url, last_connection_test_ok, "
        "last_connection_test_at, last_connection_test_detail) "
        "VALUES (?, ?, ?, ?, ?, datetime('now', '-14 minutes'), ?)",
        ("sonarr", "Sonarr (main)", 1, "http://127.0.0.1:8989", 1, "Reachable, 214 series"),
    )
