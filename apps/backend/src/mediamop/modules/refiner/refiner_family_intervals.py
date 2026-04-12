"""Refiner-local schedule interval clamps (ADR-0009: not shared with Fetcher or other modules)."""

from __future__ import annotations


def clamp_refiner_schedule_interval_seconds(n: int) -> int:
    """Bound periodic enqueue intervals for Refiner-owned families (60s .. 7d)."""

    return max(60, min(n, 7 * 24 * 3600))
