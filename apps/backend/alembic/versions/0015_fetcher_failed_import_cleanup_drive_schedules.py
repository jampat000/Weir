"""Per-app failed-import cleanup drive schedule columns on cleanup policy singleton.

Revision ID: 0015_fetcher_failed_import_cleanup_drive_schedules
Revises: 0014_fetcher_arr_connection_fields
Create Date: 2026-04-14

"""

from __future__ import annotations

from typing import Sequence, Union

import sqlalchemy as sa
from alembic import op

revision: str = "0015_fetcher_failed_import_cleanup_drive_schedules"
down_revision: Union[str, None] = "0014_fetcher_arr_connection_fields"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("radarr_cleanup_drive_schedule_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column(
            "radarr_cleanup_drive_schedule_interval_seconds",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("3600"),
        ),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("sonarr_cleanup_drive_schedule_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column(
            "sonarr_cleanup_drive_schedule_interval_seconds",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("3600"),
        ),
    )


def downgrade() -> None:
    op.drop_column("fetcher_failed_import_cleanup_policy", "sonarr_cleanup_drive_schedule_interval_seconds")
    op.drop_column("fetcher_failed_import_cleanup_policy", "sonarr_cleanup_drive_schedule_enabled")
    op.drop_column("fetcher_failed_import_cleanup_policy", "radarr_cleanup_drive_schedule_interval_seconds")
    op.drop_column("fetcher_failed_import_cleanup_policy", "radarr_cleanup_drive_schedule_enabled")
