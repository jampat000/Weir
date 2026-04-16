"""One persisted Pruner preview run — source of truth for candidates and outcome."""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import Boolean, DateTime, ForeignKey, Integer, String, Text, func, text
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class PrunerPreviewRun(Base):
    """Source of truth for a preview; ``pruner_scope_settings`` mirrors latest summary only."""

    __tablename__ = "pruner_preview_runs"

    id: Mapped[int] = mapped_column(Integer, primary_key=True, autoincrement=True)
    preview_run_id: Mapped[str] = mapped_column(String(36), unique=True, nullable=False)
    server_instance_id: Mapped[int] = mapped_column(
        Integer,
        ForeignKey("pruner_server_instances.id", ondelete="CASCADE"),
        nullable=False,
    )
    media_scope: Mapped[str] = mapped_column(String(16), nullable=False)
    rule_family_id: Mapped[str] = mapped_column(String(64), nullable=False)
    pruner_job_id: Mapped[int | None] = mapped_column(
        Integer,
        ForeignKey("pruner_jobs.id", ondelete="SET NULL"),
        nullable=True,
    )
    candidate_count: Mapped[int] = mapped_column(Integer, nullable=False, server_default=text("0"))
    candidates_json: Mapped[str] = mapped_column(Text, nullable=False, server_default=text("''"))
    truncated: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=text("0"))
    outcome: Mapped[str] = mapped_column(String(32), nullable=False)
    unsupported_detail: Mapped[str | None] = mapped_column(Text, nullable=True)
    error_message: Mapped[str | None] = mapped_column(Text, nullable=True)
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        nullable=False,
    )
