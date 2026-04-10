"""Refiner Pass 16: independent Radarr/Sonarr periodic cleanup-drive enqueue schedules."""

from __future__ import annotations

import asyncio
from dataclasses import replace

import pytest
from sqlalchemy import func, select
from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.config import MediaMopSettings
from mediamop.modules.refiner.jobs_model import RefinerJob
from mediamop.modules.refiner.periodic_cleanup_drive_enqueue import (
    refiner_cleanup_drive_enqueue_schedule_specs,
    run_periodic_refiner_cleanup_drive_enqueue,
    start_refiner_cleanup_drive_enqueue_schedule_tasks,
    stop_refiner_cleanup_drive_enqueue_schedule_tasks,
)
from mediamop.modules.refiner.radarr_failed_import_cleanup_job import (
    REFINER_JOB_KIND_RADARR_FAILED_IMPORT_CLEANUP_DRIVE,
    enqueue_radarr_failed_import_cleanup_drive_job,
)
from mediamop.modules.refiner.sonarr_failed_import_cleanup_job import (
    REFINER_JOB_KIND_SONARR_FAILED_IMPORT_CLEANUP_DRIVE,
    enqueue_sonarr_failed_import_cleanup_drive_job,
)

import mediamop.modules.refiner.jobs_model  # noqa: F401
import mediamop.platform.activity.models  # noqa: F401
import mediamop.platform.auth.models  # noqa: F401
from mediamop.core.db import Base


@pytest.fixture
def jobs_engine(tmp_path):
    from sqlalchemy import create_engine

    url = f"sqlite:///{tmp_path / 'schedule.sqlite'}"
    engine = create_engine(
        url,
        connect_args={"check_same_thread": False, "timeout": 30.0},
        future=True,
    )
    Base.metadata.create_all(engine)
    return engine


@pytest.fixture
def session_factory(jobs_engine):
    return sessionmaker(
        bind=jobs_engine,
        class_=Session,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        future=True,
    )


def _base_settings() -> MediaMopSettings:
    return MediaMopSettings.load()


def test_schedule_specs_radarr_only_when_radarr_enabled() -> None:
    base = _base_settings()
    s = replace(
        base,
        refiner_radarr_cleanup_drive_schedule_enabled=True,
        refiner_radarr_cleanup_drive_schedule_interval_seconds=120,
        refiner_sonarr_cleanup_drive_schedule_enabled=False,
    )
    specs = refiner_cleanup_drive_enqueue_schedule_specs(s)
    assert len(specs) == 1
    assert specs[0][0] == "radarr_failed_import_cleanup_drive"
    assert specs[0][1] == 120.0
    assert specs[0][2] is enqueue_radarr_failed_import_cleanup_drive_job


def test_schedule_specs_sonarr_only_when_sonarr_enabled() -> None:
    base = _base_settings()
    s = replace(
        base,
        refiner_radarr_cleanup_drive_schedule_enabled=False,
        refiner_sonarr_cleanup_drive_schedule_enabled=True,
        refiner_sonarr_cleanup_drive_schedule_interval_seconds=90,
    )
    specs = refiner_cleanup_drive_enqueue_schedule_specs(s)
    assert len(specs) == 1
    assert specs[0][0] == "sonarr_failed_import_cleanup_drive"
    assert specs[0][1] == 90.0
    assert specs[0][2] is enqueue_sonarr_failed_import_cleanup_drive_job


def test_schedule_specs_both_independent_when_both_enabled() -> None:
    base = _base_settings()
    s = replace(
        base,
        refiner_radarr_cleanup_drive_schedule_enabled=True,
        refiner_radarr_cleanup_drive_schedule_interval_seconds=100,
        refiner_sonarr_cleanup_drive_schedule_enabled=True,
        refiner_sonarr_cleanup_drive_schedule_interval_seconds=200,
    )
    specs = refiner_cleanup_drive_enqueue_schedule_specs(s)
    assert len(specs) == 2
    assert specs[0][1] != specs[1][1]


def test_schedule_specs_empty_when_both_disabled() -> None:
    base = _base_settings()
    s = replace(
        base,
        refiner_radarr_cleanup_drive_schedule_enabled=False,
        refiner_sonarr_cleanup_drive_schedule_enabled=False,
    )
    assert refiner_cleanup_drive_enqueue_schedule_specs(s) == []


def test_disabling_radarr_does_not_imply_sonarr_spec() -> None:
    base = _base_settings()
    s = replace(
        base,
        refiner_radarr_cleanup_drive_schedule_enabled=False,
        refiner_sonarr_cleanup_drive_schedule_enabled=True,
        refiner_sonarr_cleanup_drive_schedule_interval_seconds=60,
    )
    specs = refiner_cleanup_drive_enqueue_schedule_specs(s)
    assert len(specs) == 1
    assert specs[0][2] is enqueue_sonarr_failed_import_cleanup_drive_job


def test_periodic_radarr_enqueue_dedupes_across_ticks(session_factory) -> None:
    async def _run() -> None:
        stop = asyncio.Event()
        t0 = asyncio.create_task(
            run_periodic_refiner_cleanup_drive_enqueue(
                session_factory,
                stop_event=stop,
                interval_seconds=0.06,
                log_label="test_radarr",
                enqueue_fn=enqueue_radarr_failed_import_cleanup_drive_job,
            ),
        )
        await asyncio.sleep(0.2)
        stop.set()
        await t0

    asyncio.run(_run())

    with session_factory() as s:
        n = s.scalar(
            select(func.count()).select_from(RefinerJob).where(
                RefinerJob.job_kind == REFINER_JOB_KIND_RADARR_FAILED_IMPORT_CLEANUP_DRIVE,
            ),
        )
    assert n == 1


def test_periodic_sonarr_enqueue_runs_independently(session_factory) -> None:
    async def _run() -> None:
        stop = asyncio.Event()
        t0 = asyncio.create_task(
            run_periodic_refiner_cleanup_drive_enqueue(
                session_factory,
                stop_event=stop,
                interval_seconds=0.06,
                log_label="test_sonarr",
                enqueue_fn=enqueue_sonarr_failed_import_cleanup_drive_job,
            ),
        )
        await asyncio.sleep(0.2)
        stop.set()
        await t0

    asyncio.run(_run())

    with session_factory() as s:
        n = s.scalar(
            select(func.count()).select_from(RefinerJob).where(
                RefinerJob.job_kind == REFINER_JOB_KIND_SONARR_FAILED_IMPORT_CLEANUP_DRIVE,
            ),
        )
    assert n == 1


def test_start_schedule_tasks_respects_settings_independence(session_factory) -> None:
    async def _run() -> None:
        base = _base_settings()
        settings = replace(
            base,
            refiner_radarr_cleanup_drive_schedule_enabled=True,
            refiner_radarr_cleanup_drive_schedule_interval_seconds=3600,
            refiner_sonarr_cleanup_drive_schedule_enabled=False,
        )
        stop = asyncio.Event()
        tasks = start_refiner_cleanup_drive_enqueue_schedule_tasks(
            session_factory,
            settings,
            stop_event=stop,
        )
        assert len(tasks) == 1
        stop.set()
        await stop_refiner_cleanup_drive_enqueue_schedule_tasks(tasks)

    asyncio.run(_run())
