"""Who can reach Weir over the network (``/api/v1/suite/network-access``).

Only the Windows package has a tray to carry a change out, so the contract server (a bare install) reports the
setting as not applicable and refuses a change with the reason. A Docker run says its port mapping decides.
"""

from __future__ import annotations

import pytest

from tests.contract.support.client import API
from tests.contract.system import _helpers as h

NETWORK_ACCESS = f"{API}/suite/network-access"


@pytest.fixture(scope="module", autouse=True)
def _users(server) -> None:
    h.seed_users(server)


@pytest.fixture
def viewer(server, client_factory):
    return h.signed_in_viewer(server, client_factory)


def test_the_state_needs_a_signed_in_user(client) -> None:
    assert client.get(NETWORK_ACCESS).status_code == 401


def test_a_bare_install_reports_not_applicable_and_points_to_the_bind_option(admin) -> None:
    body = admin.get(NETWORK_ACCESS).json()

    assert body["state"] == "not_applicable"
    assert body["scope"] is None
    assert body["pending_scope"] is None
    assert body["firewall"] == "not_checked"
    assert body["addresses"] == []
    assert isinstance(body["port"], int)
    assert body["machine_name"]
    assert "--host" in body["summary"]


def test_a_viewer_can_read_the_state(viewer) -> None:
    assert viewer.get(NETWORK_ACCESS).status_code == 200


def test_a_bare_install_refuses_a_change_and_says_what_decides(admin) -> None:
    response = admin.put_csrf(NETWORK_ACCESS, {"scope": "network"})

    assert response.status_code == 409
    assert "--host" in response.json()["detail"]


def test_a_viewer_cannot_change_it(viewer) -> None:
    response = viewer.put_csrf(NETWORK_ACCESS, {"scope": "network"})

    assert response.status_code == 403


def test_a_scope_that_is_not_one_of_the_two_choices_is_refused(admin) -> None:
    response = admin.put_csrf(NETWORK_ACCESS, {"scope": "everyone"})

    assert response.status_code == 422


def test_a_change_without_a_csrf_token_is_refused(admin) -> None:
    response = admin.put(NETWORK_ACCESS, json={"scope": "network"})

    assert response.status_code == 422


def test_a_change_with_a_token_that_is_not_theirs_is_refused(admin) -> None:
    response = admin.put(NETWORK_ACCESS, json={"scope": "network", "csrf_token": "not-a-token"})

    assert response.status_code == 400


def test_docker_says_its_port_mapping_decides(server_factory, client_factory) -> None:
    docker = h.signed_in_admin(server_factory({"WEIR_RUNTIME": "docker"}), client_factory)

    response = docker.put_csrf(NETWORK_ACCESS, {"scope": "network"})

    assert response.status_code == 409
    assert response.json()["detail"].startswith("Set by Docker's port mapping")
    assert docker.get(NETWORK_ACCESS).json()["summary"].startswith("Set by Docker's port mapping")
