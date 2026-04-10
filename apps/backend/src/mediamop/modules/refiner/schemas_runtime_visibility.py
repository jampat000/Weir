"""Pydantic shapes for read-only Refiner runtime configuration (loaded settings, not liveness)."""

from __future__ import annotations

from pydantic import BaseModel, Field


class RefinerRuntimeVisibilityOut(BaseModel):
    """Settings-derived Refiner intent — not proof that runners or timed passes are active."""

    refiner_worker_count: int = Field(ge=0, le=8, description="Configured Refiner background runner count (0 means none).")
    in_process_workers_disabled: bool = Field(
        description="True when runner count is 0 (queued tasks will not start automatically).",
    )
    in_process_workers_enabled: bool = Field(
        description="True when at least one runner is configured to process queued tasks.",
    )
    worker_mode_summary: str = Field(
        description="Plain-language summary of runner count semantics (0 / 1 / >1).",
    )
    refiner_radarr_cleanup_drive_schedule_enabled: bool
    refiner_radarr_cleanup_drive_schedule_interval_seconds: int = Field(ge=60)
    refiner_sonarr_cleanup_drive_schedule_enabled: bool
    refiner_sonarr_cleanup_drive_schedule_interval_seconds: int = Field(ge=60)
    visibility_note: str = Field(
        description="Caveat: from settings only — not proof of live runners, timed passes, or app connectivity.",
    )
