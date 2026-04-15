"""Singleton ``refiner_remux_rules_settings`` for operator remux defaults (audio/subtitles).

Revision ID: 0018_refiner_remux_rules_settings
Revises: 0017_fetcher_failed_import_queue_actions
Create Date: 2026-04-15
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0018_refiner_remux_rules_settings"
down_revision: str | None = "0017_fetcher_failed_import_queue_actions"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "refiner_remux_rules_settings",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("primary_audio_lang", sa.Text(), nullable=False, server_default=sa.text("'eng'")),
        sa.Column("secondary_audio_lang", sa.Text(), nullable=False, server_default=sa.text("'jpn'")),
        sa.Column("tertiary_audio_lang", sa.Text(), nullable=False, server_default=sa.text("''")),
        sa.Column("default_audio_slot", sa.Text(), nullable=False, server_default=sa.text("'primary'")),
        sa.Column("remove_commentary", sa.Integer(), nullable=False, server_default=sa.text("1")),
        sa.Column("subtitle_mode", sa.Text(), nullable=False, server_default=sa.text("'remove_all'")),
        sa.Column("subtitle_langs_csv", sa.Text(), nullable=False, server_default=sa.text("''")),
        sa.Column("preserve_forced_subs", sa.Integer(), nullable=False, server_default=sa.text("1")),
        sa.Column("preserve_default_subs", sa.Integer(), nullable=False, server_default=sa.text("1")),
        sa.Column(
            "audio_preference_mode",
            sa.Text(),
            nullable=False,
            server_default=sa.text("'preferred_langs_quality'"),
        ),
        sa.Column(
            "updated_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("(CURRENT_TIMESTAMP)"),
            nullable=False,
        ),
        sa.CheckConstraint("id = 1", name="ck_refiner_remux_rules_settings_singleton"),
        sa.PrimaryKeyConstraint("id", name="pk_refiner_remux_rules_settings"),
    )
    op.execute(
        sa.text(
            "INSERT INTO refiner_remux_rules_settings (id, updated_at) VALUES (1, CURRENT_TIMESTAMP)"
        )
    )


def downgrade() -> None:
    op.drop_table("refiner_remux_rules_settings")
