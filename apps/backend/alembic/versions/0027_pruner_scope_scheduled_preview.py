"""Per-scope scheduled Pruner preview (interval + last enqueue metadata).

Revision ID: 0027_pruner_scope_scheduled_preview
Revises: 0026_pruner_instances_scope_preview
Create Date: 2026-04-17

Each ``pruner_scope_settings`` row owns its own schedule state for
``(server_instance_id, media_scope)`` — no shared row across scopes or instances.
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0027_pruner_scope_scheduled_preview"
down_revision: str | None = "0026_pruner_instances_scope_preview"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "pruner_scope_settings",
        sa.Column(
            "scheduled_preview_enabled",
            sa.Boolean(),
            nullable=False,
            server_default=sa.text("0"),
        ),
    )
    op.add_column(
        "pruner_scope_settings",
        sa.Column(
            "scheduled_preview_interval_seconds",
            sa.Integer(),
            nullable=False,
            server_default=sa.text("3600"),
        ),
    )
    op.add_column(
        "pruner_scope_settings",
        sa.Column("last_scheduled_preview_enqueued_at", sa.DateTime(timezone=True), nullable=True),
    )


def downgrade() -> None:
    op.drop_column("pruner_scope_settings", "last_scheduled_preview_enqueued_at")
    op.drop_column("pruner_scope_settings", "scheduled_preview_interval_seconds")
    op.drop_column("pruner_scope_settings", "scheduled_preview_enabled")
