"""Subber settings: adaptive search, HI filter, upgrade schedule, path mapping.

Revision ID: 0040_subber_settings_extended
Revises: 0039_subber_subtitle_state
Create Date: 2026-04-19
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0040_subber_settings_extended"
down_revision: str | None = "0039_subber_subtitle_state"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "subber_settings",
        sa.Column("adaptive_searching_enabled", sa.Boolean(), server_default=sa.text("1"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("adaptive_searching_delay_hours", sa.Integer(), server_default=sa.text("168"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("adaptive_searching_max_attempts", sa.Integer(), server_default=sa.text("3"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("permanent_skip_after_attempts", sa.Integer(), server_default=sa.text("10"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("exclude_hearing_impaired", sa.Boolean(), server_default=sa.text("0"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("upgrade_enabled", sa.Boolean(), server_default=sa.text("0"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("upgrade_schedule_enabled", sa.Boolean(), server_default=sa.text("0"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column(
            "upgrade_schedule_interval_seconds",
            sa.Integer(),
            server_default=sa.text("604800"),
            nullable=False,
        ),
    )
    op.add_column(
        "subber_settings",
        sa.Column("upgrade_schedule_hours_limited", sa.Boolean(), server_default=sa.text("0"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("upgrade_schedule_days", sa.String(length=200), server_default="", nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("upgrade_schedule_start", sa.String(length=5), server_default="00:00", nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("upgrade_schedule_end", sa.String(length=5), server_default="23:59", nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("upgrade_last_scheduled_at", sa.DateTime(timezone=True), nullable=True),
    )
    op.add_column(
        "subber_settings",
        sa.Column("sonarr_path_mapping_enabled", sa.Boolean(), server_default=sa.text("0"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("sonarr_path_sonarr", sa.String(length=1000), server_default="", nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("sonarr_path_subber", sa.String(length=1000), server_default="", nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("radarr_path_mapping_enabled", sa.Boolean(), server_default=sa.text("0"), nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("radarr_path_radarr", sa.String(length=1000), server_default="", nullable=False),
    )
    op.add_column(
        "subber_settings",
        sa.Column("radarr_path_subber", sa.String(length=1000), server_default="", nullable=False),
    )


def downgrade() -> None:
    op.drop_column("subber_settings", "radarr_path_subber")
    op.drop_column("subber_settings", "radarr_path_radarr")
    op.drop_column("subber_settings", "radarr_path_mapping_enabled")
    op.drop_column("subber_settings", "sonarr_path_subber")
    op.drop_column("subber_settings", "sonarr_path_sonarr")
    op.drop_column("subber_settings", "sonarr_path_mapping_enabled")
    op.drop_column("subber_settings", "upgrade_last_scheduled_at")
    op.drop_column("subber_settings", "upgrade_schedule_end")
    op.drop_column("subber_settings", "upgrade_schedule_start")
    op.drop_column("subber_settings", "upgrade_schedule_days")
    op.drop_column("subber_settings", "upgrade_schedule_hours_limited")
    op.drop_column("subber_settings", "upgrade_schedule_interval_seconds")
    op.drop_column("subber_settings", "upgrade_schedule_enabled")
    op.drop_column("subber_settings", "upgrade_enabled")
    op.drop_column("subber_settings", "exclude_hearing_impaired")
    op.drop_column("subber_settings", "permanent_skip_after_attempts")
    op.drop_column("subber_settings", "adaptive_searching_max_attempts")
    op.drop_column("subber_settings", "adaptive_searching_delay_hours")
    op.drop_column("subber_settings", "adaptive_searching_enabled")
