"""Read-only JSON shapes for Activity API."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, Field


class ActivityEventItemOut(BaseModel):
    id: int
    created_at: datetime
    event_type: str = Field(..., description="Stable type key, e.g. auth.login_succeeded.")
    module: str = Field(..., description="Source area, e.g. auth.")
    title: str
    detail: str | None = None

    model_config = {"from_attributes": True}


class ActivityRecentOut(BaseModel):
    items: list[ActivityEventItemOut] = Field(default_factory=list)
