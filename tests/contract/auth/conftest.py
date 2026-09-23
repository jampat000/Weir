"""Fixtures shared by the auth contract tests."""

from __future__ import annotations

import pytest

from tests.contract.auth import _helpers as h
from tests.contract.support.client import WeirClient


@pytest.fixture
def alice(client: WeirClient) -> WeirClient:
    """Anonymous client on the module's server, where the admin ``alice`` exists."""

    h.ensure_admin_account(client)
    return client
