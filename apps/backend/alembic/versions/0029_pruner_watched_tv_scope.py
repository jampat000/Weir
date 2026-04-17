"""Pruner scope: watched TV rule toggle (TV-tab preview/apply only).

Revision ID: 0029_pruner_watched_tv_scope
Revises: 0028_pruner_never_played_stale_scope
Create Date: 2026-04-17
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0029_pruner_watched_tv_scope"
down_revision: str | None = "0028_pruner_never_played_stale_scope"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "pruner_scope_settings",
        sa.Column("watched_tv_reported_enabled", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )


def downgrade() -> None:
    op.drop_column("pruner_scope_settings", "watched_tv_reported_enabled")
