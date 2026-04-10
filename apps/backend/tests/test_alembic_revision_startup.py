"""Alembic head enforcement at API startup (schema drift safety)."""

from __future__ import annotations

from pathlib import Path

import pytest
from alembic import command
from alembic.config import Config
from starlette.testclient import TestClient

from mediamop.api.factory import create_app
from mediamop.core.alembic_revision_check import DatabaseSchemaMismatch, require_database_at_application_head
from mediamop.core.config import MediaMopSettings
from mediamop.core.db import create_db_engine


def test_require_database_at_application_head_ok_on_migrated_db() -> None:
    settings = MediaMopSettings.load()
    engine = create_db_engine(settings)
    require_database_at_application_head(engine)


def test_api_startup_fails_without_migrations(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    monkeypatch.setenv("MEDIAMOP_SESSION_SECRET", "pytest-session-secret-32-chars-min!!")
    isolated = tmp_path / "nodb"
    isolated.mkdir()
    monkeypatch.setenv("MEDIAMOP_HOME", str(isolated))
    MediaMopSettings.load()

    with pytest.raises(DatabaseSchemaMismatch) as excinfo:
        with TestClient(create_app()):
            pass
    assert excinfo.value.kind == "unversioned"


def test_api_startup_fails_when_revision_behind_head(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    monkeypatch.setenv("MEDIAMOP_SESSION_SECRET", "pytest-session-secret-32-chars-min!!")
    home = tmp_path / "behind"
    home.mkdir()
    monkeypatch.setenv("MEDIAMOP_HOME", str(home))
    MediaMopSettings.load()

    backend = Path(__file__).resolve().parents[1]
    cfg = Config(str(backend / "alembic.ini"))
    monkeypatch.chdir(backend)
    command.upgrade(cfg, "0001_initial_auth")

    with pytest.raises(DatabaseSchemaMismatch) as excinfo:
        with TestClient(create_app()):
            pass
    assert excinfo.value.kind == "behind"
