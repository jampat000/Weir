"""Manual enqueue for ``refiner.watched_folder.remux_scan_dispatch.v1`` (``refiner_jobs`` only)."""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field


class RefinerWatchedFolderRemuxScanDispatchManualEnqueueIn(BaseModel):
    """Queue one watched-folder scan; optional enqueue of per-file ``refiner.file.remux_pass.v1`` rows."""

    model_config = ConfigDict(extra="forbid")

    csrf_token: str = Field(..., min_length=1)
    enqueue_remux_jobs: bool = Field(
        default=False,
        description=(
            "When false (default), the scan only classifies files and writes one activity summary — "
            "no remux jobs are queued. When true, eligible files (verdict proceed) also enqueue remux jobs."
        ),
    )
    remux_dry_run: bool = Field(
        default=True,
        description=(
            "Forwarded to each enqueued ``refiner.file.remux_pass.v1`` payload as ``dry_run``. "
            "Ignored when enqueue_remux_jobs is false. When false, a saved output folder is required."
        ),
    )


class RefinerWatchedFolderRemuxScanDispatchManualEnqueueOut(BaseModel):
    model_config = ConfigDict(extra="forbid")

    ok: bool = True
    job_id: int
    dedupe_key: str
    job_kind: str
