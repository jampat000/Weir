"""Manual enqueue for ``trimmer.trim_plan.constraints_check.v1`` (``trimmer_jobs`` only)."""

from __future__ import annotations

from pydantic import BaseModel, Field, field_validator, model_validator


class TrimSegmentIn(BaseModel):
    start_sec: float = Field(..., ge=0)
    end_sec: float = Field(..., description="Must be strictly greater than start_sec.")

    @model_validator(mode="after")
    def _end_after_start(self) -> TrimSegmentIn:
        if self.end_sec <= self.start_sec:
            msg = "each segment needs end_sec > start_sec"
            raise ValueError(msg)
        return self


class TrimmerTrimPlanConstraintsCheckManualEnqueueIn(BaseModel):
    """Operator posts a trim plan outline; workers validate constraints only (no media I/O)."""

    csrf_token: str = Field(..., min_length=1)
    segments: list[TrimSegmentIn] = Field(..., min_length=1)
    source_duration_sec: float | None = Field(
        default=None,
        description="When set, segments must fit within this source length and total kept time must not exceed it.",
    )

    @field_validator("source_duration_sec")
    @classmethod
    def _positive_source(cls, v: float | None) -> float | None:
        if v is None:
            return v
        if v <= 0:
            msg = "source_duration_sec must be positive when set"
            raise ValueError(msg)
        return v


class TrimmerTrimPlanConstraintsCheckManualEnqueueOut(BaseModel):
    ok: bool = True
    job_id: int
    dedupe_key: str
    job_kind: str
