"""Unit tests for ``refiner_runtime_visibility_from_settings`` (settings-derived snapshot DTO)."""

from __future__ import annotations

from dataclasses import replace

import pytest

from mediamop.core.config import MediaMopSettings
from mediamop.modules.refiner.runtime_visibility import refiner_runtime_visibility_from_settings


@pytest.fixture
def base_settings(monkeypatch: pytest.MonkeyPatch) -> MediaMopSettings:
    monkeypatch.setenv("MEDIAMOP_REFINER_WORKER_COUNT", "1")
    monkeypatch.setenv("MEDIAMOP_REFINER_RADARR_CLEANUP_DRIVE_SCHEDULE_ENABLED", "0")
    monkeypatch.setenv("MEDIAMOP_REFINER_SONARR_CLEANUP_DRIVE_SCHEDULE_ENABLED", "0")
    return MediaMopSettings.load()


def test_worker_count_zero_disabled_summary(base_settings: MediaMopSettings) -> None:
    s = replace(base_settings, refiner_worker_count=0)
    out = refiner_runtime_visibility_from_settings(s)
    assert out.refiner_worker_count == 0
    assert out.in_process_workers_disabled is True
    assert out.in_process_workers_enabled is False
    assert "off" in out.worker_mode_summary.lower()
    assert "automation" in out.worker_mode_summary.lower()


def test_worker_count_one_default_summary(base_settings: MediaMopSettings) -> None:
    s = replace(base_settings, refiner_worker_count=1)
    out = refiner_runtime_visibility_from_settings(s)
    assert out.refiner_worker_count == 1
    assert out.in_process_workers_disabled is False
    assert out.in_process_workers_enabled is True
    assert "one" in out.worker_mode_summary.lower() or "typical" in out.worker_mode_summary.lower()
    assert "guarded" not in out.worker_mode_summary.lower()


def test_worker_count_multi_cautions_operator(base_settings: MediaMopSettings) -> None:
    s = replace(base_settings, refiner_worker_count=3)
    out = refiner_runtime_visibility_from_settings(s)
    assert out.refiner_worker_count == 3
    assert "3" in out.worker_mode_summary
    assert "unusual" in out.worker_mode_summary.lower() or "confirm" in out.worker_mode_summary.lower()


def test_radarr_and_sonarr_schedules_independent(base_settings: MediaMopSettings) -> None:
    s = replace(
        base_settings,
        refiner_radarr_cleanup_drive_schedule_enabled=True,
        refiner_radarr_cleanup_drive_schedule_interval_seconds=7200,
        refiner_sonarr_cleanup_drive_schedule_enabled=False,
        refiner_sonarr_cleanup_drive_schedule_interval_seconds=3600,
    )
    out = refiner_runtime_visibility_from_settings(s)
    assert out.refiner_radarr_cleanup_drive_schedule_enabled is True
    assert out.refiner_radarr_cleanup_drive_schedule_interval_seconds == 7200
    assert out.refiner_sonarr_cleanup_drive_schedule_enabled is False
    assert out.refiner_sonarr_cleanup_drive_schedule_interval_seconds == 3600


def test_visibility_note_present(base_settings: MediaMopSettings) -> None:
    out = refiner_runtime_visibility_from_settings(base_settings)
    note = out.visibility_note.lower()
    assert "saved" in note
    assert "live" in note
