"""Persisted activity events for the read-only Activity feed (Stage 8)."""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import DateTime, Integer, String, Text, func
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class ActivityEvent(Base):
    """One row per surfaced platform event — narrow fields only, no generic JSON payload."""

    __tablename__ = "activity_events"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        nullable=False,
    )
    event_type: Mapped[str] = mapped_column(String(64), nullable=False)
    module: Mapped[str] = mapped_column(String(32), nullable=False)
    title: Mapped[str] = mapped_column(String(255), nullable=False)
    detail: Mapped[str | None] = mapped_column(Text, nullable=True)
