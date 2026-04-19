"""Subber providers: nullable priority + five new provider rows.

priority becomes nullable (NULL = not yet assigned by user).
Existing rows that were auto-seeded with 0-4 are reset to NULL
so the user starts fresh with the new priority assignment logic.
Five new providers are inserted with NULL priority.

Revision ID: 0043_subber_providers_nullable_priority_new_providers
Revises: 0042_subber_subtitle_state
Create Date: 2026-04-20
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0043_subber_providers_nullable_priority_new_providers"
down_revision: str | None = "0042_subber_subtitle_state"
branch_labels = None
depends_on = None


def upgrade() -> None:
    with op.batch_alter_table("subber_providers") as batch_op:
        batch_op.alter_column(
            "priority",
            existing_type=sa.Integer(),
            nullable=True,
            server_default=None,
        )

    op.execute(sa.text("UPDATE subber_providers SET priority = NULL"))

    new_providers = [
        "gestdown",
        "subdl",
        "subsource",
        "subf2m",
        "yify",
    ]
    bind = op.get_bind()
    for pk in new_providers:
        bind.execute(
            sa.text(
                "INSERT OR IGNORE INTO subber_providers "
                "(provider_key, enabled, priority, credentials_ciphertext) "
                "VALUES (:pk, 0, NULL, '')",
            ),
            {"pk": pk},
        )


def downgrade() -> None:
    op.execute(sa.text("UPDATE subber_providers SET priority = 0 WHERE priority IS NULL"))
    with op.batch_alter_table("subber_providers") as batch_op:
        batch_op.alter_column(
            "priority",
            existing_type=sa.Integer(),
            nullable=False,
            server_default=sa.text("0"),
        )
