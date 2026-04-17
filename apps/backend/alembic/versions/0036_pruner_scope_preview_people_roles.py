"""Pruner scope: JSON people credit roles for preview narrowing (cast, director, …).

Revision ID: 0036_pruner_scope_preview_people_roles
Revises: 0035_pruner_low_rating_provider_thresholds
Create Date: 2026-04-18
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0036_pruner_scope_preview_people_roles"
down_revision: str | None = "0035_pruner_low_rating_provider_thresholds"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "pruner_scope_settings",
        sa.Column(
            "preview_include_people_roles_json",
            sa.Text(),
            nullable=False,
            server_default=sa.text("'[\"cast\"]'"),
        ),
    )


def downgrade() -> None:
    op.drop_column("pruner_scope_settings", "preview_include_people_roles_json")
