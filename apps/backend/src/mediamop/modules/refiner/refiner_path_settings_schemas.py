"""HTTP schemas for Refiner path settings (singleton row)."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, Field


class RefinerPathSettingsOut(BaseModel):
    refiner_watched_folder: str | None
    refiner_work_folder: str | None
    refiner_output_folder: str
    resolved_default_work_folder: str
    effective_work_folder: str
    refiner_tv_watched_folder: str | None
    refiner_tv_work_folder: str | None
    refiner_tv_output_folder: str | None
    resolved_default_tv_work_folder: str
    effective_tv_work_folder: str
    updated_at: datetime


class RefinerPathSettingsPutIn(BaseModel):
    refiner_watched_folder: str | None = None
    refiner_work_folder: str | None = None
    refiner_output_folder: str = Field(..., min_length=1)
    refiner_tv_paths_included: bool = Field(
        default=False,
        description=(
            "When true, TV watched/work/output fields are validated and persisted (clear TV by sending empty values). "
            "When false, existing TV path columns are left unchanged (backward-compatible movie-only saves)."
        ),
    )
    refiner_tv_watched_folder: str | None = None
    refiner_tv_work_folder: str | None = None
    refiner_tv_output_folder: str | None = None
    csrf_token: str = Field(..., min_length=1)
