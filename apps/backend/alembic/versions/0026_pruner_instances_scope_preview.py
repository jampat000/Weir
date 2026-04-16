"""Pruner server instances, per-scope settings, and preview run history.

Revision ID: 0026_pruner_instances_scope_preview
Revises: 0025_pruner_jobs_drop_trimmer_jobs
Create Date: 2026-04-17

Preview contract:
- ``pruner_preview_runs`` is the source of truth for each preview run (candidates JSON, outcome).
- ``pruner_scope_settings`` holds denormalized latest-summary fields for fast reads; ``last_preview_run_id``
  always references the latest ``pruner_preview_runs.id`` for the same (server_instance_id, media_scope).
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0026_pruner_instances_scope_preview"
down_revision: str | None = "0025_pruner_jobs_drop_trimmer_jobs"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "pruner_server_instances",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("provider", sa.String(length=16), nullable=False),
        sa.Column("display_name", sa.String(length=200), nullable=False),
        sa.Column("base_url", sa.String(length=512), nullable=False),
        sa.Column("credentials_ciphertext", sa.Text(), nullable=False),
        sa.Column("enabled", sa.Boolean(), nullable=False, server_default=sa.text("1")),
        sa.Column("last_connection_test_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("last_connection_test_ok", sa.Boolean(), nullable=True),
        sa.Column("last_connection_test_detail", sa.Text(), nullable=True),
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
        sa.PrimaryKeyConstraint("id", name="pk_pruner_server_instances"),
    )

    op.create_table(
        "pruner_preview_runs",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("preview_run_id", sa.String(length=36), nullable=False),
        sa.Column("server_instance_id", sa.Integer(), nullable=False),
        sa.Column("media_scope", sa.String(length=16), nullable=False),
        sa.Column("rule_family_id", sa.String(length=64), nullable=False),
        sa.Column("pruner_job_id", sa.Integer(), nullable=True),
        sa.Column("candidate_count", sa.Integer(), nullable=False, server_default=sa.text("0")),
        sa.Column("candidates_json", sa.Text(), nullable=False, server_default=sa.text("''")),
        sa.Column("truncated", sa.Boolean(), nullable=False, server_default=sa.text("0")),
        sa.Column("outcome", sa.String(length=32), nullable=False),
        sa.Column("unsupported_detail", sa.Text(), nullable=True),
        sa.Column("error_message", sa.Text(), nullable=True),
        sa.Column(
            "created_at",
            sa.DateTime(timezone=True),
            server_default=sa.text("(CURRENT_TIMESTAMP)"),
            nullable=False,
        ),
        sa.PrimaryKeyConstraint("id", name="pk_pruner_preview_runs"),
        sa.UniqueConstraint("preview_run_id", name="uq_pruner_preview_runs_preview_run_id"),
        sa.ForeignKeyConstraint(
            ["server_instance_id"],
            ["pruner_server_instances.id"],
            name="fk_pruner_preview_runs_server_instance_id",
            ondelete="CASCADE",
        ),
        sa.ForeignKeyConstraint(
            ["pruner_job_id"],
            ["pruner_jobs.id"],
            name="fk_pruner_preview_runs_pruner_job_id",
            ondelete="SET NULL",
        ),
    )
    op.create_index(
        "ix_pruner_preview_runs_instance_scope_created",
        "pruner_preview_runs",
        ["server_instance_id", "media_scope", "created_at"],
        unique=False,
    )

    op.create_table(
        "pruner_scope_settings",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("server_instance_id", sa.Integer(), nullable=False),
        sa.Column("media_scope", sa.String(length=16), nullable=False),
        sa.Column("missing_primary_media_reported_enabled", sa.Boolean(), nullable=False, server_default=sa.text("1")),
        sa.Column("preview_max_items", sa.Integer(), nullable=False, server_default=sa.text("500")),
        sa.Column("last_preview_run_id", sa.Integer(), nullable=True),
        sa.Column("last_preview_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("last_preview_candidate_count", sa.Integer(), nullable=True),
        sa.Column("last_preview_outcome", sa.String(length=32), nullable=True),
        sa.Column("last_preview_error", sa.Text(), nullable=True),
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
        sa.PrimaryKeyConstraint("id", name="pk_pruner_scope_settings"),
        sa.ForeignKeyConstraint(
            ["server_instance_id"],
            ["pruner_server_instances.id"],
            name="fk_pruner_scope_settings_server_instance_id",
            ondelete="CASCADE",
        ),
        sa.ForeignKeyConstraint(
            ["last_preview_run_id"],
            ["pruner_preview_runs.id"],
            name="fk_pruner_scope_settings_last_preview_run_id",
            ondelete="SET NULL",
        ),
        sa.UniqueConstraint(
            "server_instance_id",
            "media_scope",
            name="uq_pruner_scope_settings_instance_scope",
        ),
    )
    op.create_index(
        "ix_pruner_scope_settings_server_instance_id",
        "pruner_scope_settings",
        ["server_instance_id"],
        unique=False,
    )


def downgrade() -> None:
    op.drop_index("ix_pruner_scope_settings_server_instance_id", table_name="pruner_scope_settings")
    op.drop_table("pruner_scope_settings")
    op.drop_index("ix_pruner_preview_runs_instance_scope_created", table_name="pruner_preview_runs")
    op.drop_table("pruner_preview_runs")
    op.drop_table("pruner_server_instances")
