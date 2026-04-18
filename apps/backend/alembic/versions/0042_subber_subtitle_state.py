"""Add provider / upgrade tracking columns to ``subber_subtitle_state``.

Separate from ``0041_subber_providers`` (single-concern migrations).

Revision ID: 0042_subber_subtitle_state
Revises: 0041_subber_providers
Create Date: 2026-04-19
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0042_subber_subtitle_state"
down_revision: str | None = "0041_subber_providers"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "subber_subtitle_state",
        sa.Column("provider_key", sa.String(length=50), nullable=True),
    )
    op.add_column(
        "subber_subtitle_state",
        sa.Column("upgraded_at", sa.DateTime(timezone=True), nullable=True),
    )
    op.add_column(
        "subber_subtitle_state",
        sa.Column("upgrade_count", sa.Integer(), server_default=sa.text("0"), nullable=False),
    )


def downgrade() -> None:
    op.drop_column("subber_subtitle_state", "upgrade_count")
    op.drop_column("subber_subtitle_state", "upgraded_at")
    op.drop_column("subber_subtitle_state", "provider_key")
