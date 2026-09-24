"""Fixtures shared by the hand-off status, lifecycle and folder contract tests."""

from __future__ import annotations

import pytest

from tests.contract.media_managers._handoff_status_helpers import SECRET_ENV
from tests.contract.media_managers._helpers import LibraryFolders, ensure_library
from tests.contract.support.client import WeirClient


@pytest.fixture(scope="module")
def server_env() -> dict[str, str]:
    return dict(SECRET_ENV)


@pytest.fixture(scope="module")
def module_folders(tmp_path_factory: pytest.TempPathFactory) -> tuple[LibraryFolders, LibraryFolders]:
    root = tmp_path_factory.mktemp("handoff_status_libraries")
    return LibraryFolders.make(root / "movies"), LibraryFolders.make(root / "tv")


@pytest.fixture
def movies(admin: WeirClient, module_folders: tuple[LibraryFolders, LibraryFolders]) -> LibraryFolders:
    """The module server's Movies and TV libraries have watched folders; returns the Movies folders."""

    movie_folders, tv_folders = module_folders
    ensure_library(admin, name="Movies", media_type="movie", folders=movie_folders)
    ensure_library(admin, name="TV", media_type="tv", folders=tv_folders)
    return movie_folders
