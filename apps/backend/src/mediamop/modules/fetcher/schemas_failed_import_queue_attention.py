"""Read-only live queue attention snapshot for Fetcher Overview (classified failed-import rows)."""

from __future__ import annotations

from datetime import datetime

from pydantic import BaseModel, Field


class FetcherFailedImportQueueAttentionAxisOut(BaseModel):
    """TV (Sonarr) or movies (Radarr) — live queue scan when credentials exist."""

    needs_attention_count: int | None = Field(
        default=None,
        description=(
            "Count of queue rows classified to a terminal failed-import class **and** with a saved "
            "handling action other than leave_alone for that axis; null if the live queue could not be read."
        ),
    )
    last_checked_at: datetime | None = Field(
        default=None,
        description="UTC time of the successful live queue read, or last completed cleanup pass when live read failed.",
    )


class FetcherFailedImportQueueAttentionSnapshotOut(BaseModel):
    """Bounded read model for Fetcher Overview failed-import attention cards."""

    tv_shows: FetcherFailedImportQueueAttentionAxisOut
    movies: FetcherFailedImportQueueAttentionAxisOut
