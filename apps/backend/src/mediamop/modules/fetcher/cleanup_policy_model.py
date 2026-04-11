"""Persisted Fetcher failed-import removal rules (Radarr/Sonarr download queue).

Single row ``id = 1``. Seeded once from environment via
:func:`mediamop.modules.fetcher.cleanup_policy_service.load_fetcher_failed_import_cleanup_bundle`
when missing; runtime reads use this row only.
"""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import Boolean, CheckConstraint, DateTime, Integer, func
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class FetcherFailedImportCleanupPolicyRow(Base):
    __tablename__ = "fetcher_failed_import_cleanup_policy"
    __table_args__ = (CheckConstraint("id = 1", name="ck_fetcher_failed_import_cleanup_policy_singleton"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    radarr_remove_quality_rejections: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    radarr_remove_unmatched_manual_import_rejections: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        server_default="0",
    )
    radarr_remove_corrupt_imports: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    radarr_remove_failed_downloads: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    radarr_remove_failed_imports: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    sonarr_remove_quality_rejections: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    sonarr_remove_unmatched_manual_import_rejections: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        server_default="0",
    )
    sonarr_remove_corrupt_imports: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    sonarr_remove_failed_downloads: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    sonarr_remove_failed_imports: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default="0")
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        onupdate=func.now(),
        nullable=False,
    )
