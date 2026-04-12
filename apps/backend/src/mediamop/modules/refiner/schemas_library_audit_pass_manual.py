"""Manual enqueue for Refiner ``refiner.library.audit_pass.v1`` (``refiner_jobs`` only)."""

from __future__ import annotations

from pydantic import BaseModel, Field


class RefinerLibraryAuditPassManualEnqueueIn(BaseModel):
    """Operator-triggered enqueue (singleton dedupe; returns existing row when present)."""

    csrf_token: str = Field(..., min_length=1)


class RefinerLibraryAuditPassManualEnqueueOut(BaseModel):
    ok: bool = True
    job_id: int
    dedupe_key: str
    job_kind: str
