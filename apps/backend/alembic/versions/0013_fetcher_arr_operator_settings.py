"""Singleton ``fetcher_arr_operator_settings`` — Fetcher-owned Sonarr/Radarr search preferences (not credentials).

Revision ID: 0013_fetcher_arr_operator_settings
Revises: 0012_suite_settings
Create Date: 2026-04-12

"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0013_fetcher_arr_operator_settings"
down_revision: str | None = "0012_suite_settings"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "fetcher_arr_operator_settings",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("sonarr_missing_search_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("sonarr_missing_search_max_items_per_run", sa.Integer(), nullable=False, server_default=sa.text("50")),
        sa.Column("sonarr_missing_search_retry_delay_minutes", sa.Integer(), nullable=False, server_default=sa.text("1440")),
        sa.Column("sonarr_missing_search_schedule_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("sonarr_missing_search_schedule_days", sa.Text(), nullable=False, server_default=""),
        sa.Column("sonarr_missing_search_schedule_start", sa.Text(), nullable=False, server_default=sa.text("'00:00'")),
        sa.Column("sonarr_missing_search_schedule_end", sa.Text(), nullable=False, server_default=sa.text("'23:59'")),
        sa.Column(
            "sonarr_missing_search_schedule_interval_seconds",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("3600"),
        ),
        sa.Column("sonarr_upgrade_search_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("sonarr_upgrade_search_max_items_per_run", sa.Integer(), nullable=False, server_default=sa.text("50")),
        sa.Column("sonarr_upgrade_search_retry_delay_minutes", sa.Integer(), nullable=False, server_default=sa.text("1440")),
        sa.Column("sonarr_upgrade_search_schedule_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("sonarr_upgrade_search_schedule_days", sa.Text(), nullable=False, server_default=""),
        sa.Column("sonarr_upgrade_search_schedule_start", sa.Text(), nullable=False, server_default=sa.text("'00:00'")),
        sa.Column("sonarr_upgrade_search_schedule_end", sa.Text(), nullable=False, server_default=sa.text("'23:59'")),
        sa.Column(
            "sonarr_upgrade_search_schedule_interval_seconds",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("3600"),
        ),
        sa.Column("radarr_missing_search_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("radarr_missing_search_max_items_per_run", sa.Integer(), nullable=False, server_default=sa.text("50")),
        sa.Column("radarr_missing_search_retry_delay_minutes", sa.Integer(), nullable=False, server_default=sa.text("1440")),
        sa.Column("radarr_missing_search_schedule_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("radarr_missing_search_schedule_days", sa.Text(), nullable=False, server_default=""),
        sa.Column("radarr_missing_search_schedule_start", sa.Text(), nullable=False, server_default=sa.text("'00:00'")),
        sa.Column("radarr_missing_search_schedule_end", sa.Text(), nullable=False, server_default=sa.text("'23:59'")),
        sa.Column(
            "radarr_missing_search_schedule_interval_seconds",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("3600"),
        ),
        sa.Column("radarr_upgrade_search_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("radarr_upgrade_search_max_items_per_run", sa.Integer(), nullable=False, server_default=sa.text("50")),
        sa.Column("radarr_upgrade_search_retry_delay_minutes", sa.Integer(), nullable=False, server_default=sa.text("1440")),
        sa.Column("radarr_upgrade_search_schedule_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("radarr_upgrade_search_schedule_days", sa.Text(), nullable=False, server_default=""),
        sa.Column("radarr_upgrade_search_schedule_start", sa.Text(), nullable=False, server_default=sa.text("'00:00'")),
        sa.Column("radarr_upgrade_search_schedule_end", sa.Text(), nullable=False, server_default=sa.text("'23:59'")),
        sa.Column(
            "radarr_upgrade_search_schedule_interval_seconds",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("3600"),
        ),
        sa.Column(
            "updated_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("(CURRENT_TIMESTAMP)"),
            nullable=False,
        ),
        sa.CheckConstraint("id = 1", name="ck_fetcher_arr_operator_settings_singleton"),
        sa.PrimaryKeyConstraint("id", name="pk_fetcher_arr_operator_settings"),
    )
    op.execute(
        sa.text(
            "INSERT INTO fetcher_arr_operator_settings (id, updated_at) VALUES (1, CURRENT_TIMESTAMP)"
        )
    )


def downgrade() -> None:
    op.drop_table("fetcher_arr_operator_settings")
