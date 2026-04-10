"""Sonarr queue adapter → Refiner domain (TV rows only)."""

from __future__ import annotations

from mediamop.modules.refiner import (
    FileAnchorCandidate,
    file_is_owned_by_queue,
    map_radarr_queue_row_to_refiner_view,
    map_sonarr_queue_row_to_refiner_view,
    should_block_for_upstream,
)


def test_sonarr_active_paused_row_series_id_owns_and_blocks() -> None:
    row = {
        "status": "paused",
        "seriesId": 9001,
        "series": {"title": "Sample Show", "year": 2020},
    }
    v = map_sonarr_queue_row_to_refiner_view(
        row,
        candidate_series_id=9001,
    )
    assert v.applies_to_file is True
    assert v.queue_title == "Sample Show"
    assert v.is_upstream_active is True
    assert file_is_owned_by_queue((v,)) is True
    assert should_block_for_upstream((v,)) is True


def test_sonarr_sparse_missing_series_uses_top_level_title_for_anchor() -> None:
    """No ``series`` object: release title still feeds anchor when year is in the string."""
    row = {
        "status": "failed",
        "title": "Limited.Series.2024.1080p",
    }
    v = map_sonarr_queue_row_to_refiner_view(row)
    assert v.applies_to_file is False
    assert v.queue_title == "Limited.Series.2024.1080p"
    assert v.queue_year is None
    cand = FileAnchorCandidate(title="Limited Series 2024")
    assert file_is_owned_by_queue((v,), file_candidate=cand) is True


def test_sonarr_does_not_read_movie_block_for_title() -> None:
    """Sonarr adapter ignores ``movie`` so Radarr-only payloads cannot drive TV rows."""
    row = {
        "status": "completed",
        "movie": {"title": "Should Ignore", "year": 2001},
        "series": {"title": "Real Series", "year": 2018},
    }
    v = map_sonarr_queue_row_to_refiner_view(row)
    assert v.queue_title == "Real Series"
    assert v.queue_year == 2018


def test_mixed_radarr_and_sonarr_mapped_rows_domain_aggregation() -> None:
    """Cross-app queue list: each row mapped with its adapter; domain stays unified."""
    radarr_busy = {
        "status": "downloading",
        "outputPath": "C:/q/a.mkv",
        "title": "Alien.1979",
    }
    sonarr_sparse = {
        "status": "completed",
        "title": "Alien 1979 1080p",
    }
    c_busy = map_radarr_queue_row_to_refiner_view(radarr_busy, candidate_path="c:/q/a.mkv")
    c_sparse = map_sonarr_queue_row_to_refiner_view(sonarr_sparse)
    cand = FileAnchorCandidate(title="Alien 1979")
    rows = (c_busy, c_sparse)
    assert file_is_owned_by_queue(rows, file_candidate=cand) is True
    assert should_block_for_upstream(rows, file_candidate=cand) is True
