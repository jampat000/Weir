"""fetcher_failed_import_cleanup_policy singleton row for operator-editable removal rules

Revision ID: 0004_fetcher_failed_import_cleanup_policy
Revises: 0003_refiner_jobs
Create Date: 2026-04-11

"""

from __future__ import annotations

from typing import Sequence, Union

import sqlalchemy as sa
from alembic import op

revision: str = "0004_fetcher_failed_import_cleanup_policy"
down_revision: Union[str, None] = "0003_refiner_jobs"
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("id", sa.Integer(), nullable=False),
        sa.Column("radarr_remove_quality_rejections", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column(
            "radarr_remove_unmatched_manual_import_rejections",
            sa.Boolean(),
            nullable=False,
            server_default=sa.text("0"),
        ),
        sa.Column("radarr_remove_corrupt_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("radarr_remove_failed_downloads", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("radarr_remove_failed_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("sonarr_remove_quality_rejections", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column(
            "sonarr_remove_unmatched_manual_import_rejections",
            sa.Boolean(),
            nullable=False,
            server_default=sa.text("0"),
        ),
        sa.Column("sonarr_remove_corrupt_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("sonarr_remove_failed_downloads", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("sonarr_remove_failed_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column(
            "updated_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("(CURRENT_TIMESTAMP)"),
            nullable=False,
        ),
        sa.CheckConstraint("id = 1", name="ck_fetcher_failed_import_cleanup_policy_singleton"),
        sa.PrimaryKeyConstraint("id", name="pk_fetcher_failed_import_cleanup_policy"),
    )


def downgrade() -> None:
    op.drop_table("fetcher_failed_import_cleanup_policy")
