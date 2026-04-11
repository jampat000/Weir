"""Singleton timestamps for last successful Fetcher Arr search handler runs (per family)."""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import CheckConstraint, DateTime, Integer
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class FetcherSearchScheduleStateRow(Base):
    """Single row ``id = 1`` — one ``*_last_run_at`` column per search lane (four independent timestamps)."""

    __tablename__ = "fetcher_search_schedule_state"
    __table_args__ = (CheckConstraint("id = 1", name="ck_fetcher_search_schedule_state_singleton"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    sonarr_missing_last_run_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    sonarr_upgrade_last_run_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    radarr_missing_last_run_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    radarr_upgrade_last_run_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
