"""Manual Arr search enqueue (Fetcher ``fetcher_jobs``)."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field


class FetcherArrSearchManualEnqueueIn(BaseModel):
    """Operator-triggered search pass (one durable job row per request)."""

    scope: Literal["sonarr_missing", "sonarr_upgrade", "radarr_missing", "radarr_upgrade"] = Field(
        ...,
        description="Which Arr app and search flavor to run",
    )
    csrf_token: str = Field(..., min_length=1)


class FetcherArrSearchManualEnqueueOut(BaseModel):
    ok: bool = True
    job_id: int
    dedupe_key: str
    job_kind: str
