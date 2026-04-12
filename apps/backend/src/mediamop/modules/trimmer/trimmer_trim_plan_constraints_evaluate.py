"""Evaluate supplied trim-plan JSON — timing numbers only (no media I/O, no codecs)."""

from __future__ import annotations

from typing import Any


def evaluate_trim_plan_constraints(payload: dict[str, Any]) -> tuple[bool, str | None, dict[str, Any]]:
    """Return ``(ok, reason_if_invalid, detail)``.

    Rules (honest scope):

    - ``segments`` is a non-empty list of objects with numeric ``start_sec`` and ``end_sec``.
    - Each segment has ``end_sec > start_sec >= 0``.
    - Segments are ordered by ``start_sec`` ascending.
    - Segments do not overlap; abutting at an endpoint (``prev.end == next.start``) is allowed.
    - If ``source_duration_sec`` is present, every segment lies within ``[0, source_duration_sec]`` and the
      sum of segment lengths does not exceed ``source_duration_sec``.
    """

    detail: dict[str, Any] = {"segment_count": 0}
    raw_segs = payload.get("segments")
    if not isinstance(raw_segs, list) or len(raw_segs) == 0:
        return False, "payload.segments must be a non-empty list", detail

    segments: list[tuple[float, float]] = []
    for i, item in enumerate(raw_segs):
        if not isinstance(item, dict):
            return False, f"payload.segments[{i}] must be an object", detail
        try:
            start = float(item["start_sec"])
            end = float(item["end_sec"])
        except (KeyError, TypeError, ValueError):
            return False, f"payload.segments[{i}] requires numeric start_sec and end_sec", detail
        if start < 0 or end <= start:
            return False, f"payload.segments[{i}] needs 0 <= start_sec < end_sec", detail
        segments.append((start, end))

    detail["segment_count"] = len(segments)

    for i in range(1, len(segments)):
        prev_end = segments[i - 1][1]
        cur_start = segments[i][0]
        if cur_start < prev_end:
            return False, "segments overlap or are not ordered by start_sec ascending", detail

    src = payload.get("source_duration_sec")
    if src is not None:
        try:
            source_dur = float(src)
        except (TypeError, ValueError):
            return False, "source_duration_sec must be numeric when present", detail
        if source_dur <= 0:
            return False, "source_duration_sec must be positive when present", detail
        total = 0.0
        for start, end in segments:
            if end > source_dur or start > source_dur:
                return False, "segment extends past source_duration_sec", detail
            total += end - start
        if total > source_dur + 1e-9:
            return False, "sum of segment lengths exceeds source_duration_sec", detail
        detail["source_duration_sec"] = source_dur
        detail["total_kept_seconds"] = total

    detail["valid"] = True
    return True, None, detail
