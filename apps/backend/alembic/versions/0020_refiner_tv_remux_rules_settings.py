"""Add TV-specific Refiner remux rules settings columns.

Revision ID: 0020_refiner_tv_remux_rules_settings
Revises: 0019_refiner_tv_path_settings
Create Date: 2026-04-15
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0020_refiner_tv_remux_rules_settings"
down_revision: str | None = "0019_refiner_tv_path_settings"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_primary_audio_lang", sa.Text(), nullable=False, server_default=sa.text("'eng'")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_secondary_audio_lang", sa.Text(), nullable=False, server_default=sa.text("'jpn'")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_tertiary_audio_lang", sa.Text(), nullable=False, server_default=sa.text("''")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_default_audio_slot", sa.Text(), nullable=False, server_default=sa.text("'primary'")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_remove_commentary", sa.Integer(), nullable=False, server_default=sa.text("1")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_subtitle_mode", sa.Text(), nullable=False, server_default=sa.text("'remove_all'")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_subtitle_langs_csv", sa.Text(), nullable=False, server_default=sa.text("''")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_preserve_forced_subs", sa.Integer(), nullable=False, server_default=sa.text("1")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column("tv_preserve_default_subs", sa.Integer(), nullable=False, server_default=sa.text("1")),
    )
    op.add_column(
        "refiner_remux_rules_settings",
        sa.Column(
            "tv_audio_preference_mode",
            sa.Text(),
            nullable=False,
            server_default=sa.text("'preferred_langs_quality'"),
        ),
    )

    op.execute(
        sa.text(
            """
            UPDATE refiner_remux_rules_settings
            SET
              tv_primary_audio_lang = primary_audio_lang,
              tv_secondary_audio_lang = secondary_audio_lang,
              tv_tertiary_audio_lang = tertiary_audio_lang,
              tv_default_audio_slot = default_audio_slot,
              tv_remove_commentary = remove_commentary,
              tv_subtitle_mode = subtitle_mode,
              tv_subtitle_langs_csv = subtitle_langs_csv,
              tv_preserve_forced_subs = preserve_forced_subs,
              tv_preserve_default_subs = preserve_default_subs,
              tv_audio_preference_mode = audio_preference_mode
            """
        )
    )


def downgrade() -> None:
    op.drop_column("refiner_remux_rules_settings", "tv_audio_preference_mode")
    op.drop_column("refiner_remux_rules_settings", "tv_preserve_default_subs")
    op.drop_column("refiner_remux_rules_settings", "tv_preserve_forced_subs")
    op.drop_column("refiner_remux_rules_settings", "tv_subtitle_langs_csv")
    op.drop_column("refiner_remux_rules_settings", "tv_subtitle_mode")
    op.drop_column("refiner_remux_rules_settings", "tv_remove_commentary")
    op.drop_column("refiner_remux_rules_settings", "tv_default_audio_slot")
    op.drop_column("refiner_remux_rules_settings", "tv_tertiary_audio_lang")
    op.drop_column("refiner_remux_rules_settings", "tv_secondary_audio_lang")
    op.drop_column("refiner_remux_rules_settings", "tv_primary_audio_lang")
