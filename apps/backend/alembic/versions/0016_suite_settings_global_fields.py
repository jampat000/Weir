"""Add suite-level logging/timezone fields to ``suite_settings``.

Revision ID: 0016_suite_settings_global_fields
Revises: 0015_fetcher_failed_import_cleanup_drive_schedules
Create Date: 2026-04-14
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0016_suite_settings_global_fields"
down_revision: str | None = "0015_fetcher_failed_import_cleanup_drive_schedules"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "suite_settings",
        sa.Column(
            "application_logs_enabled",
            sa.Boolean(),
            nullable=False,
            server_default=sa.text("1"),
        ),
    )
    op.add_column(
        "suite_settings",
        sa.Column(
            "app_timezone",
            sa.Text(),
            nullable=False,
            server_default=sa.text("'UTC'"),
        ),
    )
    op.add_column(
        "suite_settings",
        sa.Column(
            "log_retention_days",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("30"),
        ),
    )


def downgrade() -> None:
    op.drop_column("suite_settings", "log_retention_days")
    op.drop_column("suite_settings", "app_timezone")
    op.drop_column("suite_settings", "application_logs_enabled")
