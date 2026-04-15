"""DB-backed Fetcher cleanup policy: seed once from env, then row only."""

from __future__ import annotations

import pytest
from sqlalchemy.orm import Session, sessionmaker

from mediamop.core.db import Base
from mediamop.modules.arr_failed_import.env_settings import (
    AppFailedImportCleanupPolicySettings,
    default_failed_import_cleanup_settings_bundle,
)
from mediamop.modules.arr_failed_import.queue_action import FailedImportQueueHandlingAction
from mediamop.modules.fetcher.cleanup_policy_model import FetcherFailedImportCleanupPolicyRow
from mediamop.modules.fetcher.cleanup_policy_service import (
    FailedImportDrivePolicySource,
    load_fetcher_failed_import_cleanup_bundle,
    upsert_fetcher_failed_import_cleanup_policy,
)
from mediamop.modules.fetcher.radarr_failed_import_cleanup import (
    plan_radarr_failed_import_cleanup,
    radarr_plan_requests_queue_delete,
)
from mediamop.modules.fetcher.sonarr_failed_import_cleanup import (
    plan_sonarr_failed_import_cleanup,
    sonarr_plan_requests_queue_delete,
)

import mediamop.modules.refiner.jobs_model  # noqa: F401


@pytest.fixture
def policy_engine(tmp_path):
    from sqlalchemy import create_engine

    url = f"sqlite:///{tmp_path / 'policy_plan.sqlite'}"
    engine = create_engine(
        url,
        connect_args={"check_same_thread": False, "timeout": 30.0},
        future=True,
    )
    Base.metadata.create_all(engine)
    return engine


@pytest.fixture
def session_factory(policy_engine):
    return sessionmaker(
        bind=policy_engine,
        class_=Session,
        autoflush=False,
        autocommit=False,
        expire_on_commit=False,
        future=True,
    )


def test_load_seeds_singleton_from_env_then_second_read_is_db_only(session_factory) -> None:
    env = default_failed_import_cleanup_settings_bundle()
    with session_factory() as s:
        eff1, row1 = load_fetcher_failed_import_cleanup_bundle(s, env)
        s.commit()
    assert row1.id == 1
    assert eff1.radarr.handling_failed_import is FailedImportQueueHandlingAction.LEAVE_ALONE

    with session_factory() as s:
        eff2, row2 = load_fetcher_failed_import_cleanup_bundle(s, env)
    assert row2.id == 1
    assert eff2.radarr.handling_failed_import is FailedImportQueueHandlingAction.LEAVE_ALONE
    # Mutate DB directly — env fallback must not mask this on next load.
    with session_factory() as s:
        r = s.get(FetcherFailedImportCleanupPolicyRow, 1)
        assert r is not None
        r.radarr_handling_failed_import = FailedImportQueueHandlingAction.REMOVE_ONLY.value
        s.commit()
    different_env = default_failed_import_cleanup_settings_bundle()
    with session_factory() as s:
        eff3, _ = load_fetcher_failed_import_cleanup_bundle(s, different_env)
    assert eff3.radarr.handling_failed_import is FailedImportQueueHandlingAction.REMOVE_ONLY


def test_upsert_after_seed_updates_db(session_factory) -> None:
    env = default_failed_import_cleanup_settings_bundle()
    with session_factory() as s:
        load_fetcher_failed_import_cleanup_bundle(s, env)
        s.commit()
    with session_factory() as s:
        upsert_fetcher_failed_import_cleanup_policy(
            s,
            env_bundle=env,
            radarr=AppFailedImportCleanupPolicySettings(
                handling_failed_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
            ),
            sonarr=AppFailedImportCleanupPolicySettings(),
            radarr_cleanup_drive_schedule_enabled=False,
            radarr_cleanup_drive_schedule_interval_seconds=3600,
            sonarr_cleanup_drive_schedule_enabled=False,
            sonarr_cleanup_drive_schedule_interval_seconds=3600,
        )
        s.commit()
    with session_factory() as s:
        eff, _ = load_fetcher_failed_import_cleanup_bundle(s, env)
    assert eff.radarr.handling_failed_import is FailedImportQueueHandlingAction.REMOVE_ONLY


def test_drive_policy_source_implements_both_axes(session_factory) -> None:
    env = default_failed_import_cleanup_settings_bundle()
    with session_factory() as s:
        upsert_fetcher_failed_import_cleanup_policy(
            s,
            env_bundle=env,
            radarr=AppFailedImportCleanupPolicySettings(),
            sonarr=AppFailedImportCleanupPolicySettings(
                handling_corrupt_import=FailedImportQueueHandlingAction.REMOVE_ONLY,
            ),
            radarr_cleanup_drive_schedule_enabled=False,
            radarr_cleanup_drive_schedule_interval_seconds=3600,
            sonarr_cleanup_drive_schedule_enabled=False,
            sonarr_cleanup_drive_schedule_interval_seconds=3600,
        )
        s.commit()
    with session_factory() as s:
        eff, _ = load_fetcher_failed_import_cleanup_bundle(s, env)
    src = FailedImportDrivePolicySource(eff)
    r_plan = plan_radarr_failed_import_cleanup(
        status_message_blob="Import failed",
        policy=src.radarr_failed_import_cleanup_policy(),
        radarr_queue_item_id=1,
    )
    assert radarr_plan_requests_queue_delete(r_plan) is False
    s_plan = plan_sonarr_failed_import_cleanup(
        status_message_blob="corrupt file",
        policy=src.sonarr_failed_import_cleanup_policy(),
        sonarr_queue_item_id=1,
    )
    assert sonarr_plan_requests_queue_delete(s_plan) is True
    assert s_plan.remove_from_client is True and s_plan.blocklist is False
