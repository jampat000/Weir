"""Unit tests for :func:`~mediamop.modules.fetcher.failed_import_runtime_visibility.failed_import_runtime_visibility_from_db`."""

from __future__ import annotations

from dataclasses import replace

import pytest
from sqlalchemy import create_engine
from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.config import MediaMopSettings
from mediamop.core.db import Base
from mediamop.modules.arr_failed_import.env_settings import (
    AppFailedImportCleanupPolicySettings,
    default_failed_import_cleanup_settings_bundle,
)
from mediamop.modules.fetcher.cleanup_policy_service import upsert_fetcher_failed_import_cleanup_policy
from mediamop.modules.fetcher.failed_import_runtime_visibility import (
    failed_import_runtime_visibility_from_db,
)


@pytest.fixture
def vis_session_factory(tmp_path, monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("MEDIAMOP_FETCHER_WORKER_COUNT", "1")
    monkeypatch.setenv("MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_DRIVE_SCHEDULE_ENABLED", "0")
    monkeypatch.setenv("MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_DRIVE_SCHEDULE_ENABLED", "0")
    url = f"sqlite:///{tmp_path / 'vis.sqlite'}"
    engine = create_engine(url, connect_args={"check_same_thread": False}, future=True)
    Base.metadata.create_all(engine)
    return sessionmaker(bind=engine, class_=Session, autoflush=False, autocommit=False, expire_on_commit=False, future=True)


@pytest.fixture
def base_settings(monkeypatch: pytest.MonkeyPatch) -> MediaMopSettings:
    monkeypatch.setenv("MEDIAMOP_FETCHER_WORKER_COUNT", "1")
    monkeypatch.setenv("MEDIAMOP_FAILED_IMPORT_RADARR_CLEANUP_DRIVE_SCHEDULE_ENABLED", "0")
    monkeypatch.setenv("MEDIAMOP_FAILED_IMPORT_SONARR_CLEANUP_DRIVE_SCHEDULE_ENABLED", "0")
    return MediaMopSettings.load()


def test_worker_count_zero_disabled_summary(vis_session_factory, base_settings: MediaMopSettings) -> None:
    s = replace(base_settings, fetcher_worker_count=0)
    with vis_session_factory() as session:
        out = failed_import_runtime_visibility_from_db(session, s)
    assert out.background_job_worker_count == 0
    assert out.in_process_workers_disabled is True
    assert out.in_process_workers_enabled is False
    assert "off" in out.worker_mode_summary.lower()
    assert "automation" in out.worker_mode_summary.lower()


def test_worker_count_one_default_summary(vis_session_factory, base_settings: MediaMopSettings) -> None:
    s = replace(base_settings, fetcher_worker_count=1)
    with vis_session_factory() as session:
        out = failed_import_runtime_visibility_from_db(session, s)
    assert out.background_job_worker_count == 1
    assert out.in_process_workers_disabled is False
    assert out.in_process_workers_enabled is True
    assert "one" in out.worker_mode_summary.lower() or "typical" in out.worker_mode_summary.lower()
    assert "guarded" not in out.worker_mode_summary.lower()


def test_worker_count_multi_cautions_operator(vis_session_factory, base_settings: MediaMopSettings) -> None:
    s = replace(base_settings, fetcher_worker_count=3)
    with vis_session_factory() as session:
        out = failed_import_runtime_visibility_from_db(session, s)
    assert out.background_job_worker_count == 3
    assert "3" in out.worker_mode_summary
    assert "unusual" in out.worker_mode_summary.lower() or "confirm" in out.worker_mode_summary.lower()


def test_radarr_and_sonarr_schedules_from_db_row(vis_session_factory, base_settings: MediaMopSettings) -> None:
    env = default_failed_import_cleanup_settings_bundle()
    with vis_session_factory() as session:
        upsert_fetcher_failed_import_cleanup_policy(
            session,
            env_bundle=env,
            radarr=AppFailedImportCleanupPolicySettings(),
            sonarr=AppFailedImportCleanupPolicySettings(),
            radarr_cleanup_drive_schedule_enabled=True,
            radarr_cleanup_drive_schedule_interval_seconds=7200,
            sonarr_cleanup_drive_schedule_enabled=False,
            sonarr_cleanup_drive_schedule_interval_seconds=3600,
        )
        session.commit()
        out = failed_import_runtime_visibility_from_db(session, base_settings)
    assert out.failed_import_radarr_cleanup_drive_schedule_enabled is True
    assert out.failed_import_radarr_cleanup_drive_schedule_interval_seconds == 7200
    assert out.failed_import_sonarr_cleanup_drive_schedule_enabled is False
    assert out.failed_import_sonarr_cleanup_drive_schedule_interval_seconds == 3600


def test_visibility_note_present(vis_session_factory, base_settings: MediaMopSettings) -> None:
    with vis_session_factory() as session:
        out = failed_import_runtime_visibility_from_db(session, base_settings)
    note = out.visibility_note.lower()
    assert "saved" in note
    assert "live" in note
