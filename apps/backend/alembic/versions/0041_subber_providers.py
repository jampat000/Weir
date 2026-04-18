"""Subber multi-provider table + subtitle state provider columns.

Revision ID: 0041_subber_providers
Revises: 0040_subber_settings_extended
Create Date: 2026-04-19
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0041_subber_providers"
down_revision: str | None = "0040_subber_settings_extended"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "subber_providers",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("provider_key", sa.String(length=50), nullable=False),
        sa.Column("enabled", sa.Boolean(), server_default=sa.text("0"), nullable=False),
        sa.Column("priority", sa.Integer(), server_default=sa.text("0"), nullable=False),
        sa.Column("credentials_ciphertext", sa.Text(), server_default="", nullable=False),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("(CURRENT_TIMESTAMP)"),
            nullable=False,
        ),
        sa.Column(
            "updated_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("(CURRENT_TIMESTAMP)"),
            nullable=False,
        ),
        sa.PrimaryKeyConstraint("id", name="pk_subber_providers"),
        sa.UniqueConstraint("provider_key", name="uq_subber_providers_provider_key"),
    )
    keys = [
        ("opensubtitles_org", 0),
        ("opensubtitles_com", 1),
        ("podnapisi", 2),
        ("subscene", 3),
        ("addic7ed", 4),
    ]
    for pk, pr in keys:
        op.execute(
            sa.text(
                "INSERT INTO subber_providers (provider_key, enabled, priority, credentials_ciphertext) "
                "VALUES (:pk, 0, :pr, '')",
            ).bindparams(pk=pk, pr=pr),
        )
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
    op.drop_table("subber_providers")
