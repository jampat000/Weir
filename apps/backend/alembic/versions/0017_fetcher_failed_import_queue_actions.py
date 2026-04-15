"""Per-class Sonarr/Radarr queue handling actions (replace boolean remove toggles).

Revision ID: 0017_fetcher_failed_import_queue_actions
Revises: 0016_suite_settings_global_fields
Create Date: 2026-04-15
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0017_fetcher_failed_import_queue_actions"
down_revision: str | None = "0016_suite_settings_global_fields"
branch_labels = None
depends_on = None

_ACTION = sa.String(40)
_DEFAULT = "leave_alone"
_REMOVE_ONLY = "remove_only"


def upgrade() -> None:
    bind = op.get_bind()
    dialect = bind.dialect.name if bind is not None else ""

    cols = [
        ("radarr_handling_quality_rejection", "radarr_remove_quality_rejections"),
        ("radarr_handling_unmatched_manual_import", "radarr_remove_unmatched_manual_import_rejections"),
        ("radarr_handling_sample_release", None),
        ("radarr_handling_corrupt_import", "radarr_remove_corrupt_imports"),
        ("radarr_handling_failed_download", "radarr_remove_failed_downloads"),
        ("radarr_handling_failed_import", "radarr_remove_failed_imports"),
        ("sonarr_handling_quality_rejection", "sonarr_remove_quality_rejections"),
        ("sonarr_handling_unmatched_manual_import", "sonarr_remove_unmatched_manual_import_rejections"),
        ("sonarr_handling_sample_release", None),
        ("sonarr_handling_corrupt_import", "sonarr_remove_corrupt_imports"),
        ("sonarr_handling_failed_download", "sonarr_remove_failed_downloads"),
        ("sonarr_handling_failed_import", "sonarr_remove_failed_imports"),
    ]

    insp = sa.inspect(bind)
    existing = {c["name"] for c in insp.get_columns("fetcher_failed_import_cleanup_policy")}

    for new_col, _old in cols:
        if new_col not in existing:
            op.add_column(
                "fetcher_failed_import_cleanup_policy",
                sa.Column(new_col, _ACTION, nullable=False, server_default=_DEFAULT),
            )

    # Migrate legacy booleans → remove_only / leave_alone (explicit *arr DELETE flags added in app code).
    legacy_present = "radarr_remove_quality_rejections" in existing
    if legacy_present:
        op.execute(
            sa.text(
                f"""
                UPDATE fetcher_failed_import_cleanup_policy SET
                radarr_handling_quality_rejection = CASE WHEN radarr_remove_quality_rejections THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                radarr_handling_unmatched_manual_import = CASE WHEN radarr_remove_unmatched_manual_import_rejections THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                radarr_handling_sample_release = '{_DEFAULT}',
                radarr_handling_corrupt_import = CASE WHEN radarr_remove_corrupt_imports THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                radarr_handling_failed_download = CASE WHEN radarr_remove_failed_downloads THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                radarr_handling_failed_import = CASE WHEN radarr_remove_failed_imports THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                sonarr_handling_quality_rejection = CASE WHEN sonarr_remove_quality_rejections THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                sonarr_handling_unmatched_manual_import = CASE WHEN sonarr_remove_unmatched_manual_import_rejections THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                sonarr_handling_sample_release = '{_DEFAULT}',
                sonarr_handling_corrupt_import = CASE WHEN sonarr_remove_corrupt_imports THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                sonarr_handling_failed_download = CASE WHEN sonarr_remove_failed_downloads THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END,
                sonarr_handling_failed_import = CASE WHEN sonarr_remove_failed_imports THEN '{_REMOVE_ONLY}' ELSE '{_DEFAULT}' END
                """,
            ),
        )

    insp = sa.inspect(bind)
    existing = {c["name"] for c in insp.get_columns("fetcher_failed_import_cleanup_policy")}

    if dialect == "sqlite":
        with op.batch_alter_table("fetcher_failed_import_cleanup_policy") as batch:
            for _new, old in cols:
                if old and old in existing:
                    batch.drop_column(old)
    else:
        for _new, old in cols:
            if old and old in existing:
                op.drop_column("fetcher_failed_import_cleanup_policy", old)

    # SQLite cannot DROP COLUMN DEFAULT via ALTER COLUMN; leave defaults on SQLite.
    if dialect != "sqlite":
        for new_col, _old in cols:
            op.alter_column(
                "fetcher_failed_import_cleanup_policy",
                new_col,
                server_default=None,
            )


def downgrade() -> None:
    bind = op.get_bind()
    dialect = bind.dialect.name if bind is not None else ""

    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("radarr_remove_quality_rejections", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column(
            "radarr_remove_unmatched_manual_import_rejections",
            sa.Boolean(),
            nullable=False,
            server_default=sa.text("0"),
        ),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("radarr_remove_corrupt_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("radarr_remove_failed_downloads", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("radarr_remove_failed_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("sonarr_remove_quality_rejections", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column(
            "sonarr_remove_unmatched_manual_import_rejections",
            sa.Boolean(),
            nullable=False,
            server_default=sa.text("0"),
        ),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("sonarr_remove_corrupt_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("sonarr_remove_failed_downloads", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )
    op.add_column(
        "fetcher_failed_import_cleanup_policy",
        sa.Column("sonarr_remove_failed_imports", sa.Boolean(), nullable=False, server_default=sa.text("0")),
    )

    op.execute(
        sa.text(
            "UPDATE fetcher_failed_import_cleanup_policy SET "
            "radarr_remove_quality_rejections = CASE WHEN radarr_handling_quality_rejection != 'leave_alone' THEN 1 ELSE 0 END, "
            "radarr_remove_unmatched_manual_import_rejections = CASE WHEN radarr_handling_unmatched_manual_import != 'leave_alone' THEN 1 ELSE 0 END, "
            "radarr_remove_corrupt_imports = CASE WHEN radarr_handling_corrupt_import != 'leave_alone' THEN 1 ELSE 0 END, "
            "radarr_remove_failed_downloads = CASE WHEN radarr_handling_failed_download != 'leave_alone' THEN 1 ELSE 0 END, "
            "radarr_remove_failed_imports = CASE WHEN radarr_handling_failed_import != 'leave_alone' THEN 1 ELSE 0 END, "
            "sonarr_remove_quality_rejections = CASE WHEN sonarr_handling_quality_rejection != 'leave_alone' THEN 1 ELSE 0 END, "
            "sonarr_remove_unmatched_manual_import_rejections = CASE WHEN sonarr_handling_unmatched_manual_import != 'leave_alone' THEN 1 ELSE 0 END, "
            "sonarr_remove_corrupt_imports = CASE WHEN sonarr_handling_corrupt_import != 'leave_alone' THEN 1 ELSE 0 END, "
            "sonarr_remove_failed_downloads = CASE WHEN sonarr_handling_failed_download != 'leave_alone' THEN 1 ELSE 0 END, "
            "sonarr_remove_failed_imports = CASE WHEN sonarr_handling_failed_import != 'leave_alone' THEN 1 ELSE 0 END",
        ),
    )

    drop_cols = [
        "radarr_handling_quality_rejection",
        "radarr_handling_unmatched_manual_import",
        "radarr_handling_sample_release",
        "radarr_handling_corrupt_import",
        "radarr_handling_failed_download",
        "radarr_handling_failed_import",
        "sonarr_handling_quality_rejection",
        "sonarr_handling_unmatched_manual_import",
        "sonarr_handling_sample_release",
        "sonarr_handling_corrupt_import",
        "sonarr_handling_failed_download",
        "sonarr_handling_failed_import",
    ]
    if dialect == "sqlite":
        with op.batch_alter_table("fetcher_failed_import_cleanup_policy") as batch:
            for c in drop_cols:
                batch.drop_column(c)
    else:
        for c in drop_cols:
            op.drop_column("fetcher_failed_import_cleanup_policy", c)

    if dialect != "sqlite":
        for c in (
            "radarr_remove_quality_rejections",
            "radarr_remove_unmatched_manual_import_rejections",
            "radarr_remove_corrupt_imports",
            "radarr_remove_failed_downloads",
            "radarr_remove_failed_imports",
            "sonarr_remove_quality_rejections",
            "sonarr_remove_unmatched_manual_import_rejections",
            "sonarr_remove_corrupt_imports",
            "sonarr_remove_failed_downloads",
            "sonarr_remove_failed_imports",
        ):
            op.alter_column("fetcher_failed_import_cleanup_policy", c, server_default=None)
