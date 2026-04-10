"""Radarr queue adapter → Refiner domain (movie rows only)."""

from __future__ import annotations

from mediamop.modules.refiner import (
    FileAnchorCandidate,
    file_is_owned_by_queue,
    map_radarr_queue_row_to_refiner_view,
    should_block_for_upstream,
)


def test_radarr_active_downloading_row_path_match_owns_and_blocks() -> None:
    row = {
        "status": "downloading",
        "outputPath": "D:\\Media\\Film.mkv",
        "movie": {"title": "Solaris", "year": 1972},
    }
    v = map_radarr_queue_row_to_refiner_view(
        row,
        candidate_path=r"D:/media/film.mkv",
    )
    assert v.applies_to_file is True
    assert v.is_import_pending is False
    assert v.is_upstream_active is True
    assert v.blocking_suppressed_for_import_wait is False
    assert file_is_owned_by_queue((v,)) is True
    assert should_block_for_upstream((v,)) is True


def test_radarr_import_pending_row_owns_by_path_suppression_carried() -> None:
    row = {
        "status": "importPending",
        "outputPath": "/data/complete/movie.mkv",
        "blockingSuppressedForImportWait": True,
        "movie": {"title": "Nashville", "year": 1975},
    }
    v = map_radarr_queue_row_to_refiner_view(row, candidate_path="/data/complete/movie.mkv")
    assert v.is_import_pending is True
    assert v.is_upstream_active is False
    assert v.blocking_suppressed_for_import_wait is True
    assert file_is_owned_by_queue((v,)) is True
    assert should_block_for_upstream((v,)) is False


def test_radarr_downloading_row_suppressed_does_not_block_but_still_owns() -> None:
    row = {
        "status": "downloading",
        "outputPath": "/srv/queue/x.mkv",
        "blocking_suppressed_for_import_wait": True,
        "movie": {"title": "Stalker", "year": 1979},
    }
    v = map_radarr_queue_row_to_refiner_view(row, candidate_path="/srv/queue/x.mkv")
    assert v.is_upstream_active is True
    assert v.blocking_suppressed_for_import_wait is True
    assert file_is_owned_by_queue((v,)) is True
    assert should_block_for_upstream((v,)) is False


def test_radarr_completed_row_owns_via_title_year_anchor_only() -> None:
    row = {
        "status": "completed",
        "title": "The.Towering.Inferno.1974.1080p.BluRay.x264",
        "movie": {"title": "The Towering Inferno", "year": 1974},
    }
    v = map_radarr_queue_row_to_refiner_view(row, candidate_path=None)
    assert v.applies_to_file is False
    assert v.is_upstream_active is False
    assert v.queue_title == "The Towering Inferno"
    assert v.queue_year == 1974
    cand = FileAnchorCandidate(title="The Towering Inferno 1974 Remux")
    assert file_is_owned_by_queue((v,), file_candidate=cand) is True
    assert should_block_for_upstream((v,), file_candidate=cand) is False


def test_radarr_path_applicability_via_movie_id_without_path() -> None:
    row = {
        "status": "queued",
        "movieId": 42,
        "movie": {"title": "Heat", "year": 1995},
    }
    v = map_radarr_queue_row_to_refiner_view(
        row,
        candidate_path=None,
        candidate_movie_id=42,
    )
    assert v.applies_to_file is True
    assert v.is_upstream_active is True


def test_radarr_title_only_applicability_via_anchor_no_path_or_id() -> None:
    row = {
        "status": "failed",
        "movie": {"title": "The Conversation", "year": 1974},
    }
    v = map_radarr_queue_row_to_refiner_view(row)
    assert v.applies_to_file is False
    cand = FileAnchorCandidate(title="The Conversation 1974")
    assert file_is_owned_by_queue((v,), file_candidate=cand) is True
    assert should_block_for_upstream((v,), file_candidate=cand) is False


def test_radarr_tracked_download_status_fallback() -> None:
    row = {
        "trackedDownloadStatus": "Downloading",
        "outputPath": "/tmp/z.mkv",
        "movie": {"title": "Z", "year": 2001},
    }
    v = map_radarr_queue_row_to_refiner_view(row, candidate_path="/tmp/z.mkv")
    assert v.is_upstream_active is True


def test_radarr_does_not_use_series_for_title_when_movie_present() -> None:
    """Radarr adapter ignores ``series`` so TV-shaped keys cannot override movie."""
    row = {
        "status": "completed",
        "movie": {"title": "Correct Movie", "year": 2020},
        "series": {"title": "Wrong Series", "year": 1999},
    }
    v = map_radarr_queue_row_to_refiner_view(row)
    assert v.queue_title == "Correct Movie"
    assert v.queue_year == 2020
