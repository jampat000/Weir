"""Singleton ``refiner_remux_rules_settings`` (id = 1) — operator defaults for ``refiner.file.remux_pass.v1`` planning."""

from __future__ import annotations

from datetime import datetime

from sqlalchemy import CheckConstraint, DateTime, Integer, Text, func
from sqlalchemy.orm import Mapped, mapped_column

from mediamop.core.db import Base


class RefinerRemuxRulesSettingsRow(Base):
    __tablename__ = "refiner_remux_rules_settings"
    __table_args__ = (CheckConstraint("id = 1", name="ck_refiner_remux_rules_settings_singleton"),)

    id: Mapped[int] = mapped_column(Integer, primary_key=True)
    primary_audio_lang: Mapped[str] = mapped_column(Text, nullable=False)
    secondary_audio_lang: Mapped[str] = mapped_column(Text, nullable=False)
    tertiary_audio_lang: Mapped[str] = mapped_column(Text, nullable=False)
    default_audio_slot: Mapped[str] = mapped_column(Text, nullable=False)
    remove_commentary: Mapped[int] = mapped_column(Integer, nullable=False)
    subtitle_mode: Mapped[str] = mapped_column(Text, nullable=False)
    subtitle_langs_csv: Mapped[str] = mapped_column(Text, nullable=False)
    preserve_forced_subs: Mapped[int] = mapped_column(Integer, nullable=False)
    preserve_default_subs: Mapped[int] = mapped_column(Integer, nullable=False)
    audio_preference_mode: Mapped[str] = mapped_column(Text, nullable=False)
    tv_primary_audio_lang: Mapped[str] = mapped_column(Text, nullable=False)
    tv_secondary_audio_lang: Mapped[str] = mapped_column(Text, nullable=False)
    tv_tertiary_audio_lang: Mapped[str] = mapped_column(Text, nullable=False)
    tv_default_audio_slot: Mapped[str] = mapped_column(Text, nullable=False)
    tv_remove_commentary: Mapped[int] = mapped_column(Integer, nullable=False)
    tv_subtitle_mode: Mapped[str] = mapped_column(Text, nullable=False)
    tv_subtitle_langs_csv: Mapped[str] = mapped_column(Text, nullable=False)
    tv_preserve_forced_subs: Mapped[int] = mapped_column(Integer, nullable=False)
    tv_preserve_default_subs: Mapped[int] = mapped_column(Integer, nullable=False)
    tv_audio_preference_mode: Mapped[str] = mapped_column(Text, nullable=False)
    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        onupdate=func.now(),
        nullable=False,
    )
