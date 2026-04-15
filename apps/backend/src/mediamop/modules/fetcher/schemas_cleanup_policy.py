"""Pydantic schemas for Fetcher failed-import cleanup policy API."""

from __future__ import annotations

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

from mediamop.modules.arr_failed_import.env_settings import AppFailedImportCleanupPolicySettings
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction

FailedImportQueueHandlingActionLiteral = Literal[
    "leave_alone",
    "remove_only",
    "blocklist_only",
    "remove_and_blocklist",
]


_FAILED_IMPORT_CLEANUP_SCHEDULE_IV_MAX = 7 * 24 * 3600


class FailedImportCleanupPolicyAxisOut(BaseModel):
    model_config = ConfigDict(extra="forbid")

    handling_quality_rejection: FailedImportQueueHandlingActionLiteral = Field(
        description="Radarr/Sonarr: action when the queue message classifies as a quality / not-an-upgrade rejection.",
    )
    handling_unmatched_manual_import: FailedImportQueueHandlingActionLiteral = Field(
        description="Action when the queue message classifies as unmatched / manual import required.",
    )
    handling_sample_release: FailedImportQueueHandlingActionLiteral = Field(
        description="Action when the queue message classifies as a sample / obvious junk release.",
    )
    handling_corrupt_import: FailedImportQueueHandlingActionLiteral
    handling_failed_download: FailedImportQueueHandlingActionLiteral
    handling_failed_import: FailedImportQueueHandlingActionLiteral
    cleanup_drive_schedule_enabled: bool = Field(
        description="Whether timed failed-import queue passes run for this app.",
    )
    cleanup_drive_schedule_interval_seconds: int = Field(
        ge=60,
        le=_FAILED_IMPORT_CLEANUP_SCHEDULE_IV_MAX,
        description="Seconds between timed queue passes for this app (independent per app).",
    )


class FetcherFailedImportCleanupPolicyOut(BaseModel):
    model_config = ConfigDict(extra="forbid")

    movies: FailedImportCleanupPolicyAxisOut = Field(description="Radarr (movies) per-class queue handling.")
    tv_shows: FailedImportCleanupPolicyAxisOut = Field(description="Sonarr (TV) per-class queue handling.")
    updated_at: datetime = Field(description="When this row was last written (including initial seed).")


class FailedImportCleanupPolicyAxisIn(BaseModel):
    model_config = ConfigDict(extra="forbid")

    handling_quality_rejection: FailedImportQueueHandlingActionLiteral
    handling_unmatched_manual_import: FailedImportQueueHandlingActionLiteral
    handling_sample_release: FailedImportQueueHandlingActionLiteral
    handling_corrupt_import: FailedImportQueueHandlingActionLiteral
    handling_failed_download: FailedImportQueueHandlingActionLiteral
    handling_failed_import: FailedImportQueueHandlingActionLiteral
    cleanup_drive_schedule_enabled: bool
    cleanup_drive_schedule_interval_seconds: int = Field(ge=60, le=_FAILED_IMPORT_CLEANUP_SCHEDULE_IV_MAX)

    @field_validator(
        "handling_quality_rejection",
        "handling_unmatched_manual_import",
        "handling_sample_release",
        "handling_corrupt_import",
        "handling_failed_download",
        "handling_failed_import",
        mode="before",
    )
    @classmethod
    def _normalize_action_in(cls, v: object) -> str:
        if isinstance(v, FailedImportQueueHandlingAction):
            return v.value
        if isinstance(v, str):
            s = v.strip().lower()
            FailedImportQueueHandlingAction(s)  # validate
            return s
        raise TypeError("expected str or FailedImportQueueHandlingAction")

    def to_app_settings(self) -> AppFailedImportCleanupPolicySettings:
        return AppFailedImportCleanupPolicySettings(
            handling_quality_rejection=FailedImportQueueHandlingAction(self.handling_quality_rejection),
            handling_unmatched_manual_import=FailedImportQueueHandlingAction(self.handling_unmatched_manual_import),
            handling_sample_release=FailedImportQueueHandlingAction(self.handling_sample_release),
            handling_corrupt_import=FailedImportQueueHandlingAction(self.handling_corrupt_import),
            handling_failed_download=FailedImportQueueHandlingAction(self.handling_failed_download),
            handling_failed_import=FailedImportQueueHandlingAction(self.handling_failed_import),
        )


class FetcherFailedImportCleanupPolicyPutIn(BaseModel):
    model_config = ConfigDict(extra="forbid")

    movies: FailedImportCleanupPolicyAxisIn
    tv_shows: FailedImportCleanupPolicyAxisIn
    csrf_token: str


class FetcherFailedImportCleanupPolicyAxisPutIn(FailedImportCleanupPolicyAxisIn):
    """Save one app axis (Sonarr TV or Radarr movies) without sending the other axis in the body."""

    csrf_token: str = Field(..., min_length=1)
