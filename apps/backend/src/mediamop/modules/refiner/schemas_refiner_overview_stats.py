from __future__ import annotations

from pydantic import BaseModel, Field


class RefinerOverviewStatsOut(BaseModel):
    window_days: int = Field(default=30, ge=1, le=3650)
    files_processed: int = Field(ge=0)
    success_rate_percent: float = Field(ge=0.0, le=100.0)
    space_saved_gb: float | None = None
    space_saved_available: bool = False
    space_saved_note: str = Field(default="Space-saved totals are not persisted in current Refiner run history.")
