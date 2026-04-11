"""Per-item cooldown audit for Fetcher Arr search commands (one row per dispatched id).

``app`` + ``action`` identify the search **lane** (four combinations: sonarr/radarr × missing/upgrade).
Cooldown eligibility is scoped to ``(app, action, item_type, item_id)`` — no global or cross-lane key.
"""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import DateTime, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class FetcherArrActionLog(Base):
    """Cooldown log row: lane = ``(app, action)`` with ``item_type`` + ``item_id``."""

    __tablename__ = "fetcher_arr_action_log"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        nullable=False,
    )
    app: Mapped[str] = mapped_column(String(16), nullable=False)
    action: Mapped[str] = mapped_column(String(32), nullable=False)
    item_type: Mapped[str] = mapped_column(String(16), nullable=False)
    item_id: Mapped[int] = mapped_column(Integer, nullable=False)
