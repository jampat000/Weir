"""Read-only Refiner runtime settings snapshot (from :class:`~mediamop.core.config.MediaMopSettings` at process start)."""

from __future__ import annotations

from pydantic import BaseModel, Field


class RefinerRuntimeSettingsOut(BaseModel):
    """What this API process was configured to run for ``refiner_jobs`` in-process workers only."""

    in_process_refiner_worker_count: int = Field(
        ge=0,
        le=8,
        description="Mirrors MEDIAMOP_REFINER_WORKER_COUNT after clamping — Refiner lane only.",
    )
    in_process_workers_disabled: bool = Field(
        description="True when worker count is 0 (no in-process Refiner worker tasks).",
    )
    in_process_workers_enabled: bool = Field(
        description="True when at least one in-process Refiner worker task is configured.",
    )
    worker_mode_summary: str = Field(
        description="Plain-language summary for 0 / 1 / >1 Refiner workers.",
    )
    sqlite_throughput_note: str = Field(
        description="Honest caveat about SQLite single-writer behavior when count > 1.",
    )
    configuration_note: str = Field(
        description="How operators change the value (env + restart); not an in-app editor.",
    )
    visibility_note: str = Field(
        description="Caveat: from settings loaded at startup — not a live probe of worker threads.",
    )
