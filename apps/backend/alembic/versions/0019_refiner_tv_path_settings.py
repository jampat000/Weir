"""Add optional TV Refiner path columns (Movies paths stay on existing columns)."""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision = "0019_refiner_tv_path_settings"
down_revision = "0018_refiner_remux_rules_settings"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "refiner_path_settings",
        sa.Column("refiner_tv_watched_folder", sa.Text(), nullable=True),
    )
    op.add_column(
        "refiner_path_settings",
        sa.Column("refiner_tv_work_folder", sa.Text(), nullable=True),
    )
    op.add_column(
        "refiner_path_settings",
        sa.Column("refiner_tv_output_folder", sa.Text(), nullable=True),
    )


def downgrade() -> None:
    op.drop_column("refiner_path_settings", "refiner_tv_output_folder")
    op.drop_column("refiner_path_settings", "refiner_tv_work_folder")
    op.drop_column("refiner_path_settings", "refiner_tv_watched_folder")
