"""HTTP shapes for Fetcher Sonarr/Radarr search preferences (SQLite) plus connection panels (DB + env fallback)."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field


class FetcherArrSearchLaneOut(BaseModel):
    model_config = ConfigDict(extra="forbid")

    enabled: bool = Field(description="Whether this automatic search lane is turned on.")
    max_items_per_run: int = Field(ge=1, le=1000)
    retry_delay_minutes: int = Field(ge=1, le=525600)
    schedule_enabled: bool = Field(description="When on, searches only run inside the days and times below.")
    schedule_days: str = Field(description="Comma-separated weekdays, e.g. Mon,Tue. Leave empty to mean every day.")
    schedule_start: str = Field(max_length=5)
    schedule_end: str = Field(max_length=5)
    schedule_interval_seconds: int = Field(ge=60, le=604800)


class FetcherArrConnectionPanelOut(BaseModel):
    model_config = ConfigDict(extra="forbid")

    enabled: bool
    base_url: str = Field(description="Address saved in MediaMop for this app (may be empty to use the server file).")
    api_key_is_saved: bool = Field(description="Whether an API key is already stored encrypted for this app.")
    last_test_ok: bool | None = Field(description="Last in-panel connection check outcome, if any.")
    last_test_at: datetime | None = None
    last_test_detail: str | None = None
    status_headline: str = Field(
        description="Plain-language headline for the connection status area (not checked / not set up / failed / OK).",
    )
    effective_base_url: str | None = Field(
        default=None,
        description="Address MediaMop will actually use after applying saved values and the server file.",
    )


class FetcherArrConnectionPutIn(BaseModel):
    model_config = ConfigDict(extra="forbid")

    csrf_token: str = Field(..., min_length=1)
    enabled: bool
    base_url: str = Field(default="", max_length=2000)
    api_key: str = Field(
        default="",
        max_length=8000,
        description="When empty, an existing saved key is kept. When non-empty, replaces the saved key.",
    )


class FetcherArrSearchLaneIn(BaseModel):
    model_config = ConfigDict(extra="forbid")

    enabled: bool
    max_items_per_run: int = Field(ge=1, le=1000)
    retry_delay_minutes: int = Field(ge=1, le=525600)
    schedule_enabled: bool
    schedule_days: str = Field(max_length=2000)
    schedule_start: str = Field(max_length=5)
    schedule_end: str = Field(max_length=5)
    schedule_interval_seconds: int = Field(ge=60, le=604800)


class FetcherArrOperatorSettingsOut(BaseModel):
    model_config = ConfigDict(extra="forbid")

    sonarr_missing: FetcherArrSearchLaneOut
    sonarr_upgrade: FetcherArrSearchLaneOut
    radarr_missing: FetcherArrSearchLaneOut
    radarr_upgrade: FetcherArrSearchLaneOut
    schedule_timezone: str = Field(description="Time zone name used for schedule windows (from server configuration).")
    sonarr_connection: FetcherArrConnectionPanelOut
    radarr_connection: FetcherArrConnectionPanelOut
    connection_note: str = Field(
        description="Short note on Off vs On, encryption, and fallback to the server configuration file.",
    )
    interval_restart_note: str = Field(
        description="Explains that changing how often automatic checks are queued may need an API restart.",
    )
    sonarr_server_configured: bool
    radarr_server_configured: bool
    sonarr_server_url: str | None = Field(description="Sonarr address from configuration (read-only).")
    radarr_server_url: str | None = Field(description="Radarr address from configuration (read-only).")
    updated_at: datetime


class FetcherArrOperatorSettingsPutIn(BaseModel):
    model_config = ConfigDict(extra="forbid")

    csrf_token: str = Field(..., min_length=1)
    sonarr_missing: FetcherArrSearchLaneIn
    sonarr_upgrade: FetcherArrSearchLaneIn
    radarr_missing: FetcherArrSearchLaneIn
    radarr_upgrade: FetcherArrSearchLaneIn


class FetcherArrOperatorSettingsLanePutIn(BaseModel):
    """Save a single automatic search lane (independent from the other three)."""

    model_config = ConfigDict(extra="forbid")

    csrf_token: str = Field(..., min_length=1)
    lane: FetcherArrSearchLaneIn


class FetcherArrConnectionTestIn(BaseModel):
    model_config = ConfigDict(extra="forbid")

    csrf_token: str = Field(..., min_length=1)
    app: str = Field(..., description="sonarr or radarr")
    enabled: bool | None = Field(
        default=None,
        description="When set, tests draft fields like PUT …/arr-connection/* (with base_url/api_key); omit for stored settings only.",
    )
    base_url: str | None = Field(default=None, max_length=2000)
    api_key: str | None = Field(default=None, max_length=8000)


class FetcherArrConnectionTestOut(BaseModel):
    model_config = ConfigDict(extra="forbid")

    ok: bool
    message: str
