"""Fetcher Sonarr/Radarr connection fields on singleton (encrypted API keys, last test snapshot).

Revision ID: 0014_fetcher_arr_connection_fields
Revises: 0013_fetcher_arr_operator_settings
Create Date: 2026-04-13

"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0014_fetcher_arr_connection_fields"
down_revision: str | None = "0013_fetcher_arr_operator_settings"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "fetcher_arr_operator_settings",
        sa.Column("sonarr_connection_enabled", sa.Boolean(), nullable=False, server_default=sa.text("1")),
    )
    op.add_column(
        "fetcher_arr_operator_settings",
        sa.Column("sonarr_connection_base_url", sa.Text(), nullable=False, server_default=""),
    )
    op.add_column("fetcher_arr_operator_settings", sa.Column("sonarr_connection_api_key_ciphertext", sa.Text(), nullable=True))
    op.add_column("fetcher_arr_operator_settings", sa.Column("sonarr_last_connection_test_ok", sa.Boolean(), nullable=True))
    op.add_column(
        "fetcher_arr_operator_settings",
        sa.Column("sonarr_last_connection_test_at", sa.DateTime(timezone=True), nullable=True),
    )
    op.add_column("fetcher_arr_operator_settings", sa.Column("sonarr_last_connection_test_detail", sa.Text(), nullable=True))

    op.add_column(
        "fetcher_arr_operator_settings",
        sa.Column("radarr_connection_enabled", sa.Boolean(), nullable=False, server_default=sa.text("1")),
    )
    op.add_column(
        "fetcher_arr_operator_settings",
        sa.Column("radarr_connection_base_url", sa.Text(), nullable=False, server_default=""),
    )
    op.add_column("fetcher_arr_operator_settings", sa.Column("radarr_connection_api_key_ciphertext", sa.Text(), nullable=True))
    op.add_column("fetcher_arr_operator_settings", sa.Column("radarr_last_connection_test_ok", sa.Boolean(), nullable=True))
    op.add_column(
        "fetcher_arr_operator_settings",
        sa.Column("radarr_last_connection_test_at", sa.DateTime(timezone=True), nullable=True),
    )
    op.add_column("fetcher_arr_operator_settings", sa.Column("radarr_last_connection_test_detail", sa.Text(), nullable=True))


def downgrade() -> None:
    op.drop_column("fetcher_arr_operator_settings", "radarr_last_connection_test_detail")
    op.drop_column("fetcher_arr_operator_settings", "radarr_last_connection_test_at")
    op.drop_column("fetcher_arr_operator_settings", "radarr_last_connection_test_ok")
    op.drop_column("fetcher_arr_operator_settings", "radarr_connection_api_key_ciphertext")
    op.drop_column("fetcher_arr_operator_settings", "radarr_connection_base_url")
    op.drop_column("fetcher_arr_operator_settings", "radarr_connection_enabled")

    op.drop_column("fetcher_arr_operator_settings", "sonarr_last_connection_test_detail")
    op.drop_column("fetcher_arr_operator_settings", "sonarr_last_connection_test_at")
    op.drop_column("fetcher_arr_operator_settings", "sonarr_last_connection_test_ok")
    op.drop_column("fetcher_arr_operator_settings", "sonarr_connection_api_key_ciphertext")
    op.drop_column("fetcher_arr_operator_settings", "sonarr_connection_base_url")
    op.drop_column("fetcher_arr_operator_settings", "sonarr_connection_enabled")
