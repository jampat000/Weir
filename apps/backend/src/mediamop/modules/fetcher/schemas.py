"""Schemas for read-only Fetcher operational overview."""

from __future__ import annotations

from pydantic import BaseModel, Field

from mediamop.platform.activity.schemas import ActivityEventItemOut


class FetcherConnectionOut(BaseModel):
    configured: bool
    target_display: str | None = None
    reachable: bool | None = None
    http_status: int | None = None
    latency_ms: float | None = None
    fetcher_app: str | None = None
    fetcher_version: str | None = None
    detail: str | None = None


class FetcherOperationalOverviewOut(BaseModel):
    """Read-mostly operational slice from current probe + persisted probe events."""

    status_label: str = Field(..., description="One-line operational status for current Fetcher connectivity.")
    status_detail: str = Field(..., description="Short operator-facing explanation of current status.")
    connection: FetcherConnectionOut
    latest_probe_event: ActivityEventItemOut | None = None
    recent_probe_events: list[ActivityEventItemOut] = Field(default_factory=list)
