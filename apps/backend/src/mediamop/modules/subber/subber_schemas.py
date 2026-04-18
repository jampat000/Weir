"""Pydantic schemas for Subber HTTP APIs."""

from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, Field


class SubberJobsInspectionRow(BaseModel):
    id: int
    dedupe_key: str
    job_kind: str
    status: str
    scope: str | None = None
    payload_json: str | None = None
    last_error: str | None = None
    created_at: datetime
    updated_at: datetime


class SubberJobsInspectionOut(BaseModel):
    jobs: list[SubberJobsInspectionRow]
    default_recent_slice: bool


class SubberSubtitleLangStateOut(BaseModel):
    state_id: int
    language_code: str
    status: str
    subtitle_path: str | None = None
    last_searched_at: datetime | None = None
    search_count: int
    source: str | None = None
    provider_key: str | None = None
    upgrade_count: int = 0


class SubberTvEpisodeOut(BaseModel):
    file_path: str
    episode_number: int | None
    episode_title: str | None
    languages: list[SubberSubtitleLangStateOut]


class SubberTvSeasonOut(BaseModel):
    season_number: int | None
    episodes: list[SubberTvEpisodeOut]


class SubberTvShowOut(BaseModel):
    show_title: str
    seasons: list[SubberTvSeasonOut]


class SubberTvLibraryOut(BaseModel):
    shows: list[SubberTvShowOut]


class SubberMovieRowOut(BaseModel):
    file_path: str
    movie_title: str | None
    movie_year: int | None
    languages: list[SubberSubtitleLangStateOut]


class SubberMoviesLibraryOut(BaseModel):
    movies: list[SubberMovieRowOut]


class SubberSettingsOut(BaseModel):
    enabled: bool
    opensubtitles_username: str
    opensubtitles_password_set: bool
    opensubtitles_api_key_set: bool
    sonarr_base_url: str
    sonarr_api_key_set: bool
    radarr_base_url: str
    radarr_api_key_set: bool
    language_preferences: list[str]
    subtitle_folder: str
    tv_schedule_enabled: bool
    tv_schedule_interval_seconds: int
    tv_schedule_hours_limited: bool
    tv_schedule_days: str
    tv_schedule_start: str
    tv_schedule_end: str
    movies_schedule_enabled: bool
    movies_schedule_interval_seconds: int
    movies_schedule_hours_limited: bool
    movies_schedule_days: str
    movies_schedule_start: str
    movies_schedule_end: str
    tv_last_scheduled_scan_enqueued_at: datetime | None = None
    movies_last_scheduled_scan_enqueued_at: datetime | None = None
    adaptive_searching_enabled: bool = True
    adaptive_searching_delay_hours: int = 168
    adaptive_searching_max_attempts: int = 3
    permanent_skip_after_attempts: int = 10
    exclude_hearing_impaired: bool = False
    upgrade_enabled: bool = False
    upgrade_schedule_enabled: bool = False
    upgrade_schedule_interval_seconds: int = 604800
    upgrade_schedule_hours_limited: bool = False
    upgrade_schedule_days: str = ""
    upgrade_schedule_start: str = "00:00"
    upgrade_schedule_end: str = "23:59"
    upgrade_last_scheduled_at: datetime | None = None
    sonarr_path_mapping_enabled: bool = False
    sonarr_path_sonarr: str = ""
    sonarr_path_subber: str = ""
    radarr_path_mapping_enabled: bool = False
    radarr_path_radarr: str = ""
    radarr_path_subber: str = ""
    fetcher_sonarr_base_url_hint: str = ""
    fetcher_radarr_base_url_hint: str = ""


class SubberSettingsPutIn(BaseModel):
    enabled: bool | None = None
    opensubtitles_username: str | None = None
    opensubtitles_password: str | None = None
    opensubtitles_api_key: str | None = None
    sonarr_base_url: str | None = None
    sonarr_api_key: str | None = None
    radarr_base_url: str | None = None
    radarr_api_key: str | None = None
    language_preferences: list[str] | None = None
    subtitle_folder: str | None = None
    tv_schedule_enabled: bool | None = None
    tv_schedule_interval_seconds: int | None = Field(None, ge=60, le=7 * 24 * 3600)
    tv_schedule_hours_limited: bool | None = None
    tv_schedule_days: str | None = None
    tv_schedule_start: str | None = None
    tv_schedule_end: str | None = None
    movies_schedule_enabled: bool | None = None
    movies_schedule_interval_seconds: int | None = Field(None, ge=60, le=7 * 24 * 3600)
    movies_schedule_hours_limited: bool | None = None
    movies_schedule_days: str | None = None
    movies_schedule_start: str | None = None
    movies_schedule_end: str | None = None
    adaptive_searching_enabled: bool | None = None
    adaptive_searching_delay_hours: int | None = Field(None, ge=1, le=24 * 365)
    adaptive_searching_max_attempts: int | None = Field(None, ge=1, le=1000)
    permanent_skip_after_attempts: int | None = Field(None, ge=1, le=100_000)
    exclude_hearing_impaired: bool | None = None
    upgrade_enabled: bool | None = None
    upgrade_schedule_enabled: bool | None = None
    upgrade_schedule_interval_seconds: int | None = Field(None, ge=60, le=365 * 24 * 3600)
    upgrade_schedule_hours_limited: bool | None = None
    upgrade_schedule_days: str | None = None
    upgrade_schedule_start: str | None = None
    upgrade_schedule_end: str | None = None
    sonarr_path_mapping_enabled: bool | None = None
    sonarr_path_sonarr: str | None = None
    sonarr_path_subber: str | None = None
    radarr_path_mapping_enabled: bool | None = None
    radarr_path_radarr: str | None = None
    radarr_path_subber: str | None = None


class SubberTestConnectionOut(BaseModel):
    ok: bool
    message: str


class SubberOverviewOut(BaseModel):
    total_tracked: int
    found: int
    missing: int
    searching: int
    skipped: int
    searches_today: int
    upgraded_tracks: int = 0
    per_language: list[dict[str, int | str]]


class SubberProviderOut(BaseModel):
    provider_key: str
    display_name: str
    enabled: bool
    priority: int
    requires_account: bool
    has_credentials: bool


class SubberProviderPutIn(BaseModel):
    enabled: bool | None = None
    priority: int | None = None
    username: str | None = None
    password: str | None = None
    api_key: str | None = None
