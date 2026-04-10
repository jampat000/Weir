"""Refiner worker bounds — shared by :mod:`mediamop.core.config` without import cycles."""


def clamp_refiner_worker_count(raw: int) -> int:
    """Enforce 1..8 workers (SQLite-first; single worker remains the safe default)."""

    if raw < 1:
        return 1
    return min(8, raw)
