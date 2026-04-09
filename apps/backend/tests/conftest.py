"""Pytest fixtures for MediaMop backend integration tests."""

from __future__ import annotations

import os

import pytest
from starlette.testclient import TestClient

from mediamop.api.factory import create_app
from tests.integration_helpers import seed_admin_user, seed_viewer_user


@pytest.fixture(autouse=True)
def ensure_session_secret(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv(
        "MEDIAMOP_SESSION_SECRET",
        os.environ.get("MEDIAMOP_SESSION_SECRET", "pytest-session-secret-32-chars-min!!"),
    )


@pytest.fixture
def client_with_admin() -> TestClient:
    seed_admin_user()
    app = create_app()
    with TestClient(app) as c:
        yield c


@pytest.fixture
def client_with_viewer() -> TestClient:
    seed_viewer_user()
    app = create_app()
    with TestClient(app) as c:
        yield c
