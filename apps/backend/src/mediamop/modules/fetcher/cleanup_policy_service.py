"""Fetcher failed-import queue handling policy — single source of truth in SQLite.

The singleton row ``id = 1`` holds runtime values. If it is missing, it is **seeded once** from the
process environment bundle (``MediaMopSettings.failed_import_cleanup_env`` at load time). After
that, all reads use the database row only — no ongoing env fallback for per-class handling.

Per-app timed cleanup drive enable/interval are also stored on this row (independent Radarr vs Sonarr).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from mediamop.core.config import clamp_failed_import_cleanup_drive_schedule_interval_seconds
from mediamop.modules.arr_failed_import.env_settings import (
    AppFailedImportCleanupPolicySettings,
    FailedImportCleanupSettingsBundle,
)
from mediamop.modules.arr_failed_import.policy import FailedImportCleanupPolicy
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction
from mediamop.modules.fetcher.cleanup_policy_model import FetcherFailedImportCleanupPolicyRow


def _stored_action(raw: str) -> FailedImportQueueHandlingAction:
    return FailedImportQueueHandlingAction(raw.strip().lower())


@dataclass(frozen=True, slots=True)
class FailedImportDrivePolicySource:
    """Supplies Radarr and Sonarr policies for live download-queue drives."""

    bundle: FailedImportCleanupSettingsBundle

    def radarr_failed_import_cleanup_policy(self) -> FailedImportCleanupPolicy:
        return self.bundle.radarr_policy()

    def sonarr_failed_import_cleanup_policy(self) -> FailedImportCleanupPolicy:
        return self.bundle.sonarr_policy()


def _row_to_bundle(row: FetcherFailedImportCleanupPolicyRow) -> FailedImportCleanupSettingsBundle:
    return FailedImportCleanupSettingsBundle(
        radarr=AppFailedImportCleanupPolicySettings(
            handling_quality_rejection=_stored_action(row.radarr_handling_quality_rejection),
            handling_unmatched_manual_import=_stored_action(row.radarr_handling_unmatched_manual_import),
            handling_sample_release=_stored_action(row.radarr_handling_sample_release),
            handling_corrupt_import=_stored_action(row.radarr_handling_corrupt_import),
            handling_failed_download=_stored_action(row.radarr_handling_failed_download),
            handling_failed_import=_stored_action(row.radarr_handling_failed_import),
        ),
        sonarr=AppFailedImportCleanupPolicySettings(
            handling_quality_rejection=_stored_action(row.sonarr_handling_quality_rejection),
            handling_unmatched_manual_import=_stored_action(row.sonarr_handling_unmatched_manual_import),
            handling_sample_release=_stored_action(row.sonarr_handling_sample_release),
            handling_corrupt_import=_stored_action(row.sonarr_handling_corrupt_import),
            handling_failed_download=_stored_action(row.sonarr_handling_failed_download),
            handling_failed_import=_stored_action(row.sonarr_handling_failed_import),
        ),
    )


def _new_row_from_env(
    env_bundle: FailedImportCleanupSettingsBundle,
    *,
    radarr_cleanup_drive_schedule_enabled: bool,
    radarr_cleanup_drive_schedule_interval_seconds: int,
    sonarr_cleanup_drive_schedule_enabled: bool,
    sonarr_cleanup_drive_schedule_interval_seconds: int,
) -> FetcherFailedImportCleanupPolicyRow:
    r, s = env_bundle.radarr, env_bundle.sonarr
    return FetcherFailedImportCleanupPolicyRow(
        id=1,
        radarr_handling_quality_rejection=r.handling_quality_rejection.value,
        radarr_handling_unmatched_manual_import=r.handling_unmatched_manual_import.value,
        radarr_handling_sample_release=r.handling_sample_release.value,
        radarr_handling_corrupt_import=r.handling_corrupt_import.value,
        radarr_handling_failed_download=r.handling_failed_download.value,
        radarr_handling_failed_import=r.handling_failed_import.value,
        sonarr_handling_quality_rejection=s.handling_quality_rejection.value,
        sonarr_handling_unmatched_manual_import=s.handling_unmatched_manual_import.value,
        sonarr_handling_sample_release=s.handling_sample_release.value,
        sonarr_handling_corrupt_import=s.handling_corrupt_import.value,
        sonarr_handling_failed_download=s.handling_failed_download.value,
        sonarr_handling_failed_import=s.handling_failed_import.value,
        radarr_cleanup_drive_schedule_enabled=radarr_cleanup_drive_schedule_enabled,
        radarr_cleanup_drive_schedule_interval_seconds=radarr_cleanup_drive_schedule_interval_seconds,
        sonarr_cleanup_drive_schedule_enabled=sonarr_cleanup_drive_schedule_enabled,
        sonarr_cleanup_drive_schedule_interval_seconds=sonarr_cleanup_drive_schedule_interval_seconds,
    )


def load_fetcher_failed_import_cleanup_bundle(
    session: Session,
    env_bundle: FailedImportCleanupSettingsBundle,
    *,
    schedule_seed: tuple[bool, int, bool, int] | None = None,
) -> tuple[FailedImportCleanupSettingsBundle, FetcherFailedImportCleanupPolicyRow]:
    """Return Fetcher failed-import policy from the persisted singleton row (``id = 1``).

    **This is the only lazy-seed entry point** for that row: if the row is missing, it is
    inserted once from ``env_bundle`` (environment-derived defaults at call time). After the
    row exists, returned per-class handling always comes from the database columns.

    ``schedule_seed`` is ``(radarr_enabled, radarr_interval_s, sonarr_enabled, sonarr_interval_s)``
    used **only when inserting** the missing row (typically from :class:`MediaMopSettings` at
    process/env defaults). Ignored when the row already exists.
    """

    row = session.get(FetcherFailedImportCleanupPolicyRow, 1)
    if row is not None:
        return _row_to_bundle(row), row

    rad_en, rad_iv, son_en, son_iv = schedule_seed or (False, 3600, False, 3600)
    rad_iv = clamp_failed_import_cleanup_drive_schedule_interval_seconds(rad_iv)
    son_iv = clamp_failed_import_cleanup_drive_schedule_interval_seconds(son_iv)

    with session.begin_nested():
        try:
            session.add(
                _new_row_from_env(
                    env_bundle,
                    radarr_cleanup_drive_schedule_enabled=rad_en,
                    radarr_cleanup_drive_schedule_interval_seconds=rad_iv,
                    sonarr_cleanup_drive_schedule_enabled=son_en,
                    sonarr_cleanup_drive_schedule_interval_seconds=son_iv,
                ),
            )
            session.flush()
        except IntegrityError:
            # Another writer created id=1 first (concurrent seed).
            pass

    row = session.get(FetcherFailedImportCleanupPolicyRow, 1)
    if row is None:
        msg = "fetcher failed-import cleanup policy singleton missing after seed"
        raise RuntimeError(msg)
    return _row_to_bundle(row), row


def _write_radarr_policy_row(row: FetcherFailedImportCleanupPolicyRow, p: AppFailedImportCleanupPolicySettings) -> None:
    row.radarr_handling_quality_rejection = p.handling_quality_rejection.value
    row.radarr_handling_unmatched_manual_import = p.handling_unmatched_manual_import.value
    row.radarr_handling_sample_release = p.handling_sample_release.value
    row.radarr_handling_corrupt_import = p.handling_corrupt_import.value
    row.radarr_handling_failed_download = p.handling_failed_download.value
    row.radarr_handling_failed_import = p.handling_failed_import.value


def _write_sonarr_policy_row(row: FetcherFailedImportCleanupPolicyRow, p: AppFailedImportCleanupPolicySettings) -> None:
    row.sonarr_handling_quality_rejection = p.handling_quality_rejection.value
    row.sonarr_handling_unmatched_manual_import = p.handling_unmatched_manual_import.value
    row.sonarr_handling_sample_release = p.handling_sample_release.value
    row.sonarr_handling_corrupt_import = p.handling_corrupt_import.value
    row.sonarr_handling_failed_download = p.handling_failed_download.value
    row.sonarr_handling_failed_import = p.handling_failed_import.value


def upsert_fetcher_failed_import_cleanup_policy(
    session: Session,
    *,
    env_bundle: FailedImportCleanupSettingsBundle,
    radarr: AppFailedImportCleanupPolicySettings,
    sonarr: AppFailedImportCleanupPolicySettings,
    radarr_cleanup_drive_schedule_enabled: bool,
    radarr_cleanup_drive_schedule_interval_seconds: int,
    sonarr_cleanup_drive_schedule_enabled: bool,
    sonarr_cleanup_drive_schedule_interval_seconds: int,
) -> FetcherFailedImportCleanupPolicyRow:
    """Ensure the singleton row exists (seed from env if needed), then overwrite with operator values."""

    load_fetcher_failed_import_cleanup_bundle(session, env_bundle)
    row = session.get(FetcherFailedImportCleanupPolicyRow, 1)
    if row is None:
        msg = "fetcher failed-import cleanup policy row missing after load"
        raise RuntimeError(msg)

    _write_radarr_policy_row(row, radarr)
    _write_sonarr_policy_row(row, sonarr)

    row.radarr_cleanup_drive_schedule_enabled = radarr_cleanup_drive_schedule_enabled
    row.radarr_cleanup_drive_schedule_interval_seconds = clamp_failed_import_cleanup_drive_schedule_interval_seconds(
        radarr_cleanup_drive_schedule_interval_seconds,
    )
    row.sonarr_cleanup_drive_schedule_enabled = sonarr_cleanup_drive_schedule_enabled
    row.sonarr_cleanup_drive_schedule_interval_seconds = clamp_failed_import_cleanup_drive_schedule_interval_seconds(
        sonarr_cleanup_drive_schedule_interval_seconds,
    )

    session.flush()
    return row


def apply_fetcher_failed_import_cleanup_policy_axis_put(
    session: Session,
    *,
    env_bundle: FailedImportCleanupSettingsBundle,
    axis: Literal["tv_shows", "movies"],
    policy: AppFailedImportCleanupPolicySettings,
    cleanup_drive_schedule_enabled: bool,
    cleanup_drive_schedule_interval_seconds: int,
) -> FetcherFailedImportCleanupPolicyRow:
    """Update only Sonarr (TV) or only Radarr (movies) columns; the other app is left as stored."""

    load_fetcher_failed_import_cleanup_bundle(session, env_bundle)
    row = session.get(FetcherFailedImportCleanupPolicyRow, 1)
    if row is None:
        msg = "fetcher failed-import cleanup policy row missing after load"
        raise RuntimeError(msg)

    iv = clamp_failed_import_cleanup_drive_schedule_interval_seconds(cleanup_drive_schedule_interval_seconds)
    if axis == "tv_shows":
        _write_sonarr_policy_row(row, policy)
        row.sonarr_cleanup_drive_schedule_enabled = cleanup_drive_schedule_enabled
        row.sonarr_cleanup_drive_schedule_interval_seconds = iv
    else:
        _write_radarr_policy_row(row, policy)
        row.radarr_cleanup_drive_schedule_enabled = cleanup_drive_schedule_enabled
        row.radarr_cleanup_drive_schedule_interval_seconds = iv

    session.flush()
    return row
