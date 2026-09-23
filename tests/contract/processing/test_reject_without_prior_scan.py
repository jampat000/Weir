"""#532: a rejected hand-off must have a Files row even when no watched-folder scan ever saw the file.

A hand-off Weir has never scanned has no ``files`` row yet, so a rejection that only updated an
existing row would be dropped everywhere except Activity. ``ProcessingRejectHandler`` upserts the row
(``RemuxPassFileState.UpsertRejectedAsync``); reading it back also relies on ``GET /processing/files``
listing ``rejected`` rows (#530).
"""

from __future__ import annotations

from pathlib import Path

import pytest

from tests.contract.processing import _helpers as h
from tests.contract.support.fake_ffmpeg import fake_media_bytes, probe


def test_a_rejection_with_no_prior_scan_still_creates_a_files_row(
    server_factory, client_factory, fake_ffmpeg, fake_managers, tmp_path: Path
) -> None:
    server = h.start_working_server(server_factory, fake_ffmpeg)
    admin = client_factory(server)
    admin.ensure_admin()
    folders = h.Folders.make(tmp_path)
    fake, library = h.deluno_setup(
        admin,
        fake_managers,
        folders,
        capabilities=["processor-reject-regrab"],
        failure_policy="reject",
    )
    release = folders.watched / "No.Prior.Scan.532"
    release.mkdir()
    source = release / "film.mkv"
    # A video with no audio at all: content the reject policy blocklists outright.
    source.write_bytes(fake_media_bytes(probe(audio_languages=())))
    rel = "No.Prior.Scan.532/film.mkv"

    # No detect_without_queueing, no enqueue_scan: nothing but the hand-off itself has ever looked
    # at this file. That is the exact scenario the issue describes.
    h.post_handoff(admin, handoff_id="handoff-noscan-532", source_path=source)
    h.wait_for_handoff_state(admin, "handoff-noscan-532", "rejected")

    row = h.wait_for_file_status(admin, library["id"], rel, "rejected", timeout_s=30)
    assert "no retainable audio" in row["status_reason"]
