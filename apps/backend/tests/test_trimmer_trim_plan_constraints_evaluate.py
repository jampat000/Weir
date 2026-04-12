"""Unit tests for trim-plan constraint evaluation (no worker, no DB)."""

from __future__ import annotations

from mediamop.modules.trimmer.trimmer_trim_plan_constraints_evaluate import evaluate_trim_plan_constraints


def test_valid_ordered_non_overlapping_segments() -> None:
    ok, reason, detail = evaluate_trim_plan_constraints(
        {"segments": [{"start_sec": 0, "end_sec": 10}, {"start_sec": 10, "end_sec": 20}]},
    )
    assert ok is True
    assert reason is None
    assert detail["segment_count"] == 2


def test_rejects_overlap() -> None:
    ok, reason, _ = evaluate_trim_plan_constraints(
        {"segments": [{"start_sec": 0, "end_sec": 10}, {"start_sec": 5, "end_sec": 15}]},
    )
    assert ok is False
    assert reason is not None
    assert "overlap" in reason.lower() or "ordered" in reason.lower()


def test_rejects_when_segments_extend_past_source_duration() -> None:
    ok, reason, _ = evaluate_trim_plan_constraints(
        {
            "segments": [{"start_sec": 0, "end_sec": 8}, {"start_sec": 8, "end_sec": 16}],
            "source_duration_sec": 10,
        },
    )
    assert ok is False
    assert reason is not None
    assert "source_duration" in reason.lower()
