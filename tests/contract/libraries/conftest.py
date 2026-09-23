"""Fixtures shared by the libraries contract tests."""

from __future__ import annotations

import pytest

from tests.contract.libraries import _helpers as h
from tests.contract.libraries import _library_view_helpers as v
from tests.contract.support.client import WeirClient


@pytest.fixture
def viewer(server, client_factory) -> WeirClient:
    """The viewer ``bob`` signed in on the module's server; the module seeds the account."""

    return h.signed_in_viewer(server, client_factory)


@pytest.fixture
def scanned(server, client_factory) -> tuple[WeirClient, int]:
    """
    A library with a small, deliberately varied scan index already recorded, and a client signed in
    afterwards: ``seed.stopped`` restarts the server on a new port, so a client made before it would be
    pointing at a port nothing is listening on any more.
    """

    library_id = v.seed_scan_index(server)
    return h.signed_in_admin(server, client_factory), library_id
