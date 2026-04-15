"""Persisted Fetcher failed-import per-class queue handling (Radarr/Sonarr download queue).

Single row ``id = 1``. Seeded once from environment via
:func:`mediamop.modules.fetcher.cleanup_policy_service.load_fetcher_failed_import_cleanup_bundle`
when missing; runtime reads use this row only.

Each ``*_handling_*`` column stores a :class:`~mediamop.modules.arr_failed_import.queue_action.FailedImportQueueHandlingAction`
value (snake_case string).
"""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import Boolean, CheckConstraint, DateTime, Integer, String, func
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class FetcherFailedImportCleanupPolicyRow(Base):
    __tablename__ = "fetcher_failed_import_cleanup_policy"
    __table_args__ = (CheckConstraint("id = 1", name="ck_fetcher_failed_import_cleanup_policy_singleton"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    radarr_handling_quality_rejection: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    radarr_handling_unmatched_manual_import: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    radarr_handling_sample_release: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    radarr_handling_corrupt_import: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    radarr_handling_failed_download: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    radarr_handling_failed_import: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    sonarr_handling_quality_rejection: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    sonarr_handling_unmatched_manual_import: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    sonarr_handling_sample_release: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    sonarr_handling_corrupt_import: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    sonarr_handling_failed_download: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    sonarr_handling_failed_import: Mapped[str] = mapped_column(String(40), nullable=False, server_default="leave_alone")
    radarr_cleanup_drive_schedule_enabled: Mapped[bool] = mapped_column(Boolean(), nullable=False, server_default="0")
    radarr_cleanup_drive_schedule_interval_seconds: Mapped[int] = mapped_column(
        Integer,
        nullable=False,
        server_default="3600",
    )
    sonarr_cleanup_drive_schedule_enabled: Mapped[bool] = mapped_column(Boolean(), nullable=False, server_default="0")
    sonarr_cleanup_drive_schedule_interval_seconds: Mapped[int] = mapped_column(
        Integer,
        nullable=False,
        server_default="3600",
    )
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        onupdate=func.now(),
        nullable=False,
    )
