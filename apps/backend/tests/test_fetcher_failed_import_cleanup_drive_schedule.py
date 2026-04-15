"""Independent Radarr/Sonarr periodic cleanup-drive enqueue schedules (DB-backed per-app loops)."""

from __future__ import annotations

import asyncio

import pytest
from sqlalchemy import func, select
from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.config import MediaMopSettings
from mediamop.modules.arr_failed_import.env_settings import (
    AppFailedImportCleanupPolicySettings,
    default_failed_import_cleanup_settings_bundle,
)
from mediamop.modules.fetcher.cleanup_policy_service import upsert_fetcher_failed_import_cleanup_policy
from mediamop.modules.fetcher.fetcher_jobs_model import FetcherJob
from mediamop.modules.fetcher import periodic_failed_import_cleanup_enqueue as periodic_enqueue_mod
from mediamop.modules.fetcher.radarr_failed_import_cleanup_job import (
    FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
    enqueue_radarr_failed_import_cleanup_drive_job,
)
from mediamop.modules.fetcher.sonarr_failed_import_cleanup_job import (
    FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE,
    enqueue_sonarr_failed_import_cleanup_drive_job,
)
from mediamop.modules.fetcher.failed_import_worker_ports import NoOpFailedImportTimedSchedulePassQueuedPort
from mediamop.modules.fetcher.periodic_failed_import_cleanup_enqueue import (
    run_periodic_fetcher_failed_import_cleanup_enqueue,
    start_fetcher_failed_import_cleanup_drive_enqueue_tasks_from_cleanup_policy_db,
    stop_fetcher_failed_import_cleanup_drive_enqueue_tasks,
)
from mediamop.platform.activity import constants as act_c
from mediamop.platform.activity.models import ActivityEvent

import mediamop.modules.fetcher.fetcher_jobs_model  # noqa: F401
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


def test_periodic_radarr_enqueue_dedupes_across_ticks(session_factory) -> None:
    async def _run() -> None:
        stop = asyncio.Event()
        t0 = asyncio.create_task(
            run_periodic_fetcher_failed_import_cleanup_enqueue(
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
            select(func.count()).select_from(FetcherJob).where(
                FetcherJob.job_kind == FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
            ),
        )
    assert n == 1


def test_periodic_radarr_production_label_one_fetcher_pass_queued_across_ticks(
    session_factory,
    failed_import_queue_worker_runtime_bundle,
) -> None:
    """Production log_label: emit timed pass-queued activity only when the dedupe job row is first created."""

    async def _run() -> None:
        stop = asyncio.Event()
        t0 = asyncio.create_task(
            run_periodic_fetcher_failed_import_cleanup_enqueue(
                session_factory,
                stop_event=stop,
                interval_seconds=0.06,
                log_label="radarr_failed_import_cleanup_drive",
                enqueue_fn=enqueue_radarr_failed_import_cleanup_drive_job,
                timed_failed_import_pass_queued=failed_import_queue_worker_runtime_bundle.timed_schedule_pass_queued,
            ),
        )
        await asyncio.sleep(0.2)
        stop.set()
        await t0

    asyncio.run(_run())

    with session_factory() as s:
        n_pass = s.scalar(
            select(func.count()).select_from(ActivityEvent).where(
                ActivityEvent.module == "fetcher",
                ActivityEvent.event_type == act_c.FETCHER_FAILED_IMPORT_PASS_QUEUED,
            ),
        )
        assert n_pass == 1


def test_periodic_sonarr_enqueue_runs_independently(session_factory) -> None:
    async def _run() -> None:
        stop = asyncio.Event()
        t0 = asyncio.create_task(
            run_periodic_fetcher_failed_import_cleanup_enqueue(
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
            select(func.count()).select_from(FetcherJob).where(
                FetcherJob.job_kind == FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE,
            ),
        )
    assert n == 1


def test_start_db_schedule_tasks_radarr_on_enqueues_sonarr_off_polls_only(session_factory) -> None:
    """Lifespan-style start always runs two loops; each loop reads only its app's columns from SQLite."""

    from dataclasses import replace

    env = default_failed_import_cleanup_settings_bundle()
    with session_factory() as s:
        upsert_fetcher_failed_import_cleanup_policy(
            s,
            env_bundle=env,
            radarr=AppFailedImportCleanupPolicySettings(),
            sonarr=AppFailedImportCleanupPolicySettings(),
            radarr_cleanup_drive_schedule_enabled=True,
            radarr_cleanup_drive_schedule_interval_seconds=60,
            sonarr_cleanup_drive_schedule_enabled=False,
            sonarr_cleanup_drive_schedule_interval_seconds=3600,
        )
        s.commit()

    async def _run() -> None:
        settings = replace(
            _base_settings(),
            failed_import_radarr_cleanup_drive_schedule_enabled=False,
            failed_import_radarr_cleanup_drive_schedule_interval_seconds=3600,
            failed_import_sonarr_cleanup_drive_schedule_enabled=False,
            failed_import_sonarr_cleanup_drive_schedule_interval_seconds=3600,
        )
        stop = asyncio.Event()
        tasks = start_fetcher_failed_import_cleanup_drive_enqueue_tasks_from_cleanup_policy_db(
            session_factory,
            stop_event=stop,
            timed_failed_import_pass_queued=NoOpFailedImportTimedSchedulePassQueuedPort(),
            settings=settings,
        )
        assert len(tasks) == 2
        await asyncio.sleep(0.15)
        stop.set()
        await stop_fetcher_failed_import_cleanup_drive_enqueue_tasks(tasks)

    asyncio.run(_run())

    with session_factory() as s:
        r = s.scalar(
            select(func.count()).select_from(FetcherJob).where(
                FetcherJob.job_kind == FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
            ),
        )
        so = s.scalar(
            select(func.count()).select_from(FetcherJob).where(
                FetcherJob.job_kind == FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE,
            ),
        )
    assert r == 1
    assert so == 0


def test_periodic_enqueue_failure_then_recovery_still_one_row(
    session_factory,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        periodic_enqueue_mod,
        "FETCHER_SCHEDULE_ENQUEUE_FAILURE_COOLDOWN_SECONDS",
        0.05,
    )
    n_ok = 0

    def flaky_enqueue(session: Session) -> FetcherJob:
        nonlocal n_ok
        n_ok += 1
        if n_ok == 1:
            raise RuntimeError("transient enqueue failure")
        return enqueue_radarr_failed_import_cleanup_drive_job(session)

    async def _run() -> None:
        stop = asyncio.Event()
        t0 = asyncio.create_task(
            run_periodic_fetcher_failed_import_cleanup_enqueue(
                session_factory,
                stop_event=stop,
                interval_seconds=0.06,
                log_label="test_radarr_flaky",
                enqueue_fn=flaky_enqueue,
            ),
        )
        await asyncio.sleep(0.35)
        stop.set()
        await t0

    asyncio.run(_run())
    assert n_ok >= 2
    with session_factory() as s:
        n = s.scalar(
            select(func.count()).select_from(FetcherJob).where(
                FetcherJob.job_kind == FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
            ),
        )
    assert n == 1


def test_radarr_enqueue_always_fails_sonarr_schedule_still_enqueues(
    session_factory,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(
        periodic_enqueue_mod,
        "FETCHER_SCHEDULE_ENQUEUE_FAILURE_COOLDOWN_SECONDS",
        0.05,
    )

    def broken_radarr(_session: Session) -> FetcherJob:
        raise RuntimeError("radarr config path broken")

    async def _run() -> None:
        stop = asyncio.Event()
        t_radarr = asyncio.create_task(
            run_periodic_fetcher_failed_import_cleanup_enqueue(
                session_factory,
                stop_event=stop,
                interval_seconds=0.06,
                log_label="radarr_broken",
                enqueue_fn=broken_radarr,
            ),
        )
        t_sonarr = asyncio.create_task(
            run_periodic_fetcher_failed_import_cleanup_enqueue(
                session_factory,
                stop_event=stop,
                interval_seconds=0.06,
                log_label="sonarr_ok",
                enqueue_fn=enqueue_sonarr_failed_import_cleanup_drive_job,
            ),
        )
        await asyncio.sleep(0.25)
        stop.set()
        await asyncio.gather(t_radarr, t_sonarr)

    asyncio.run(_run())
    with session_factory() as s:
        r = s.scalar(
            select(func.count()).select_from(FetcherJob).where(
                FetcherJob.job_kind == FAILED_IMPORT_JOB_KIND_RADARR_CLEANUP_DRIVE,
            ),
        )
        so = s.scalar(
            select(func.count()).select_from(FetcherJob).where(
                FetcherJob.job_kind == FAILED_IMPORT_JOB_KIND_SONARR_CLEANUP_DRIVE,
            ),
        )
    assert r == 0
    assert so == 1


def test_stop_schedule_tasks_does_not_hang(session_factory) -> None:
    async def _run() -> None:
        stop = asyncio.Event()
        tasks = [
            asyncio.create_task(
                run_periodic_fetcher_failed_import_cleanup_enqueue(
                    session_factory,
                    stop_event=stop,
                    interval_seconds=60.0,
                    log_label="slow_tick",
                    enqueue_fn=enqueue_radarr_failed_import_cleanup_drive_job,
                ),
            ),
        ]
        stop.set()
        await asyncio.wait_for(
            stop_fetcher_failed_import_cleanup_drive_enqueue_tasks(tasks),
            timeout=5.0,
        )

    asyncio.run(_run())
