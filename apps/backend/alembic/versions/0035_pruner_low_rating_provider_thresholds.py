"""Split Pruner low-rating ceilings: Jellyfin/Emby CommunityRating vs Plex audienceRating.

Revision ID: 0035_pruner_low_rating_provider_thresholds
Revises: 0034_pruner_scope_preview_year_studio_collection
Create Date: 2026-04-17
"""

from __future__ import annotations

import sqlalchemy as sa
from alembic import op

revision: str = "0035_pruner_low_rating_provider_thresholds"
down_revision: str | None = "0034_pruner_scope_preview_year_studio_collection"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column(
        "pruner_scope_settings",
        sa.Column(
            "watched_movie_low_rating_max_jellyfin_emby_community_rating",
            sa.Float(),
            nullable=False,
            server_default=sa.text("4.0"),
        ),
    )
    op.add_column(
        "pruner_scope_settings",
        sa.Column(
            "watched_movie_low_rating_max_plex_audience_rating",
            sa.Float(),
            nullable=False,
            server_default=sa.text("4.0"),
        ),
    )
    op.execute(
        sa.text(
            "UPDATE pruner_scope_settings SET "
            "watched_movie_low_rating_max_jellyfin_emby_community_rating = watched_movie_low_rating_max_community_rating, "
            "watched_movie_low_rating_max_plex_audience_rating = watched_movie_low_rating_max_community_rating",
        ),
    )
    op.drop_column("pruner_scope_settings", "watched_movie_low_rating_max_community_rating")


def downgrade() -> None:
    op.add_column(
        "pruner_scope_settings",
        sa.Column(
            "watched_movie_low_rating_max_community_rating",
            sa.Float(),
            nullable=False,
            server_default=sa.text("4.0"),
        ),
    )
    op.execute(
        sa.text(
            "UPDATE pruner_scope_settings SET watched_movie_low_rating_max_community_rating = "
            "watched_movie_low_rating_max_jellyfin_emby_community_rating",
        ),
    )
    op.drop_column("pruner_scope_settings", "watched_movie_low_rating_max_plex_audience_rating")
    op.drop_column("pruner_scope_settings", "watched_movie_low_rating_max_jellyfin_emby_community_rating")
