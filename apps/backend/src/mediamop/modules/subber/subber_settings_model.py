"""Singleton Subber settings (id = 1)."""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import Boolean, CheckConstraint, DateTime, Integer, String, Text, func, text
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class SubberSettingsRow(Base):
    __tablename__ = "subber_settings"
    __table_args__ = (CheckConstraint("id = 1", name="ck_subber_settings_singleton"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    opensubtitles_username: Mapped[str] = mapped_column(String(255), nullable=False, server_default="")
    opensubtitles_credentials_ciphertext: Mapped[str] = mapped_column(Text, nullable=False, server_default="")
    sonarr_base_url: Mapped[str] = mapped_column(String(500), nullable=False, server_default="")
    sonarr_credentials_ciphertext: Mapped[str] = mapped_column(Text, nullable=False, server_default="")
    radarr_base_url: Mapped[str] = mapped_column(String(500), nullable=False, server_default="")
    radarr_credentials_ciphertext: Mapped[str] = mapped_column(Text, nullable=False, server_default="")
    language_preferences_json: Mapped[str] = mapped_column(
        Text,
        nullable=False,
        server_default=text("'[\"en\"]'"),
    )
    subtitle_folder: Mapped[str] = mapped_column(String(1000), nullable=False, server_default="")
    tv_schedule_enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    tv_schedule_interval_seconds: Mapped[int] = mapped_column(Integer, nullable=False, server_default="21600")
    tv_schedule_hours_limited: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    tv_schedule_days: Mapped[str] = mapped_column(String(200), nullable=False, server_default="")
    tv_schedule_start: Mapped[str] = mapped_column(String(5), nullable=False, server_default="00:00")
    tv_schedule_end: Mapped[str] = mapped_column(String(5), nullable=False, server_default="23:59")
    movies_schedule_enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    movies_schedule_interval_seconds: Mapped[int] = mapped_column(Integer, nullable=False, server_default="21600")
    movies_schedule_hours_limited: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    movies_schedule_days: Mapped[str] = mapped_column(String(200), nullable=False, server_default="")
    movies_schedule_start: Mapped[str] = mapped_column(String(5), nullable=False, server_default="00:00")
    movies_schedule_end: Mapped[str] = mapped_column(String(5), nullable=False, server_default="23:59")
    tv_last_scheduled_scan_enqueued_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    movies_last_scheduled_scan_enqueued_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True),
        nullable=True,
    )
    adaptive_searching_enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="1")
    adaptive_searching_delay_hours: Mapped[int] = mapped_column(Integer, nullable=False, server_default="168")
    adaptive_searching_max_attempts: Mapped[int] = mapped_column(Integer, nullable=False, server_default="3")
    permanent_skip_after_attempts: Mapped[int] = mapped_column(Integer, nullable=False, server_default="10")
    exclude_hearing_impaired: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    upgrade_enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    upgrade_schedule_enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    upgrade_schedule_interval_seconds: Mapped[int] = mapped_column(
        Integer,
        nullable=False,
        server_default="604800",
    )
    upgrade_schedule_hours_limited: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    upgrade_schedule_days: Mapped[str] = mapped_column(String(200), nullable=False, server_default="")
    upgrade_schedule_start: Mapped[str] = mapped_column(String(5), nullable=False, server_default="00:00")
    upgrade_schedule_end: Mapped[str] = mapped_column(String(5), nullable=False, server_default="23:59")
    upgrade_last_scheduled_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    sonarr_path_mapping_enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    sonarr_path_sonarr: Mapped[str] = mapped_column(String(1000), nullable=False, server_default="")
    sonarr_path_subber: Mapped[str] = mapped_column(String(1000), nullable=False, server_default="")
    radarr_path_mapping_enabled: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    radarr_path_radarr: Mapped[str] = mapped_column(String(1000), nullable=False, server_default="")
    radarr_path_subber: Mapped[str] = mapped_column(String(1000), nullable=False, server_default="")
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        nullable=False,
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        onupdate=func.now(),
        nullable=False,
    )
