"""In-memory view of ``fetcher_arr_operator_settings`` for Arr search handlers (no credentials)."""

from __future__ import annotations

from dataclasses import dataclass

from sqlalchemy import select
from sqlalchemy.orm import Session

from mediamop.modules.fetcher.fetcher_arr_operator_settings_model import FetcherArrOperatorSettingsRow
from mediamop.modules.fetcher.fetcher_arr_search_schedule_window import DAY_NAMES


@dataclass(frozen=True, slots=True)
class FetcherArrSearchLanePrefs:
    """One missing or upgrade lane (Sonarr or Radarr)."""

    enabled: bool
    max_items_per_run: int
    retry_delay_minutes: int
    schedule_enabled: bool
    schedule_days: str
    schedule_start: str
    schedule_end: str
    schedule_interval_seconds: int


@dataclass(frozen=True, slots=True)
class FetcherArrSearchOperatorPrefs:
    """All four lanes — read from the SQLite singleton."""

    sonarr_missing: FetcherArrSearchLanePrefs
    sonarr_upgrade: FetcherArrSearchLanePrefs
    radarr_missing: FetcherArrSearchLanePrefs
    radarr_upgrade: FetcherArrSearchLanePrefs


def ensure_fetcher_arr_operator_settings_row(session: Session) -> FetcherArrOperatorSettingsRow:
    row = session.scalars(select(FetcherArrOperatorSettingsRow).where(FetcherArrOperatorSettingsRow.id == 1)).one_or_none()
    if row is None:
        row = FetcherArrOperatorSettingsRow(id=1)
        session.add(row)
        session.flush()
    return row


def _lane_from_row(
    row: FetcherArrOperatorSettingsRow,
    *,
    enabled_attr: str,
    max_attr: str,
    retry_attr: str,
    sched_en_attr: str,
    days_attr: str,
    start_attr: str,
    end_attr: str,
    iv_attr: str,
) -> FetcherArrSearchLanePrefs:
    return FetcherArrSearchLanePrefs(
        enabled=bool(getattr(row, enabled_attr)),
        max_items_per_run=max(1, int(getattr(row, max_attr) or 1)),
        retry_delay_minutes=max(1, int(getattr(row, retry_attr) or 1)),
        schedule_enabled=bool(getattr(row, sched_en_attr)),
        schedule_days=(getattr(row, days_attr) or "").strip(),
        schedule_start=(getattr(row, start_attr) or "00:00").strip(),
        schedule_end=(getattr(row, end_attr) or "23:59").strip(),
        schedule_interval_seconds=max(60, int(getattr(row, iv_attr) or 60)),
    )


def load_fetcher_arr_search_operator_prefs(session: Session) -> FetcherArrSearchOperatorPrefs:
    row = ensure_fetcher_arr_operator_settings_row(session)
    return FetcherArrSearchOperatorPrefs(
        sonarr_missing=_lane_from_row(
            row,
            enabled_attr="sonarr_missing_search_enabled",
            max_attr="sonarr_missing_search_max_items_per_run",
            retry_attr="sonarr_missing_search_retry_delay_minutes",
            sched_en_attr="sonarr_missing_search_schedule_enabled",
            days_attr="sonarr_missing_search_schedule_days",
            start_attr="sonarr_missing_search_schedule_start",
            end_attr="sonarr_missing_search_schedule_end",
            iv_attr="sonarr_missing_search_schedule_interval_seconds",
        ),
        sonarr_upgrade=_lane_from_row(
            row,
            enabled_attr="sonarr_upgrade_search_enabled",
            max_attr="sonarr_upgrade_search_max_items_per_run",
            retry_attr="sonarr_upgrade_search_retry_delay_minutes",
            sched_en_attr="sonarr_upgrade_search_schedule_enabled",
            days_attr="sonarr_upgrade_search_schedule_days",
            start_attr="sonarr_upgrade_search_schedule_start",
            end_attr="sonarr_upgrade_search_schedule_end",
            iv_attr="sonarr_upgrade_search_schedule_interval_seconds",
        ),
        radarr_missing=_lane_from_row(
            row,
            enabled_attr="radarr_missing_search_enabled",
            max_attr="radarr_missing_search_max_items_per_run",
            retry_attr="radarr_missing_search_retry_delay_minutes",
            sched_en_attr="radarr_missing_search_schedule_enabled",
            days_attr="radarr_missing_search_schedule_days",
            start_attr="radarr_missing_search_schedule_start",
            end_attr="radarr_missing_search_schedule_end",
            iv_attr="radarr_missing_search_schedule_interval_seconds",
        ),
        radarr_upgrade=_lane_from_row(
            row,
            enabled_attr="radarr_upgrade_search_enabled",
            max_attr="radarr_upgrade_search_max_items_per_run",
            retry_attr="radarr_upgrade_search_retry_delay_minutes",
            sched_en_attr="radarr_upgrade_search_schedule_enabled",
            days_attr="radarr_upgrade_search_schedule_days",
            start_attr="radarr_upgrade_search_schedule_start",
            end_attr="radarr_upgrade_search_schedule_end",
            iv_attr="radarr_upgrade_search_schedule_interval_seconds",
        ),
    )


def validate_schedule_days_csv(raw: str) -> str:
    """Return normalized comma-separated weekday tokens, or raise ValueError with plain message."""

    s = (raw or "").strip()
    if not s:
        return ""
    tokens = [t.strip() for t in s.split(",") if t.strip()]
    bad = [t for t in tokens if t not in DAY_NAMES]
    if bad:
        msg = "Days must be written like Mon, Tue, Wed with commas between them."
        raise ValueError(msg)
    return ",".join(tokens)


def normalize_hhmm(raw: str, *, fallback: str) -> str:
    t = (raw or "").strip()
    if not t:
        return fallback
    parts = t.split(":")
    if len(parts) != 2:
        msg = "Times must look like 09:30 (hour and minute)."
        raise ValueError(msg)
    try:
        h = int(parts[0])
        m = int(parts[1])
    except ValueError as e:
        msg = "Times must look like 09:30 (hour and minute)."
        raise ValueError(msg) from e
    if not (0 <= h <= 23 and 0 <= m <= 59):
        msg = "Hour must be 0–23 and minute must be 0–59."
        raise ValueError(msg)
    return f"{h:02d}:{m:02d}"
