"""``AuditPublicMixin``: the public HTTP contract, bootstrap/sign-in, and the authenticated
read-only API surface. Assumes ``AuditCore`` in the same instance (see ``audit_core.py``).
"""

from __future__ import annotations

import re
from urllib.parse import urljoin

from .config import AUDIT_PASSWORD, AUDIT_USER, BASE_URL, DOCKER_CONTAINER, EXPECTED_VERSION, read_docker_setup_code


class AuditPublicMixin:
    def public_contract(self) -> None:
        health = self.get("/health")
        self.assert_common_headers(health, "/health")
        health_body = health.json()
        self.require(health_body.get("status") == "ok", "health status is not ok")
        self.record("public health endpoint and headers")

        readiness = self.get("/ready")
        self.assert_common_headers(readiness, "/ready")
        readiness_body = readiness.json()
        self.require(readiness_body.get("ready") is True, "readiness is not true")
        self.require(
            readiness_body.get("status") == "ready", "readiness status is wrong"
        )
        self.require(
            set(readiness_body).issubset({"ready", "status"}),
            "public readiness leaks detailed dependency state",
        )
        self.record("public readiness endpoint is minimal and truthful")

        openapi = self.get("/openapi.json")
        openapi_headers = {key.lower(): value for key, value in openapi.headers.items()}
        self.require(
            "server" not in openapi_headers, "/openapi.json exposes a Server header"
        )
        openapi_body = openapi.json()
        self.require(
            "openapi" in openapi_body, "OpenAPI document is missing its version"
        )
        self.require(
            str(openapi_body.get("info", {}).get("version", "")).strip()
            == EXPECTED_VERSION,
            f"packaged server does not report version {EXPECTED_VERSION}",
        )
        self.record("OpenAPI document and packaged version")

        index = self.get("/")
        index_headers = {key.lower(): value for key, value in index.headers.items()}
        self.require("server" not in index_headers, "index exposes a Server header")
        self.require(
            "no-store" in index_headers.get("cache-control", "").lower(),
            "index is not marked no-store",
        )
        html = index.text()
        assets = re.findall(r'(?:src|href)=["\']([^"\']*assets/[^"\']+)["\']', html)
        self.require(bool(assets), "index did not reference a built asset")
        asset_path = assets[0]
        asset = self.get(asset_path, headers={"Accept-Encoding": "gzip, br"})
        asset_headers = {key.lower(): value for key, value in asset.headers.items()}
        self.require(
            "server" not in asset_headers, "static asset exposes a Server header"
        )
        self.require(
            "immutable" in asset_headers.get("cache-control", "").lower(),
            "hashed static asset is not immutable-cacheable",
        )
        if asset_headers.get("content-encoding"):
            self.require(
                "accept-encoding" in asset_headers.get("vary", "").lower(),
                "compressed static asset is missing Vary: Accept-Encoding",
            )
        self.record("static asset compression/cache contract")

    def bootstrap_and_sign_in(self) -> None:
        self.page.goto(BASE_URL + "/", wait_until="domcontentloaded")
        self.settle()

        wizard_skip = self.page.get_by_test_id("setup-wizard-skip")
        shell_ready = self.page.get_by_test_id("shell-ready")
        wizard_or_shell = wizard_skip.or_(shell_ready).first

        setup_user = self.page.get_by_test_id("setup-username")
        if setup_user.count():
            self.visible(setup_user, "first-time setup form is visible")
            setup_user.fill(AUDIT_USER)
            self.page.get_by_test_id("setup-password").fill(AUDIT_PASSWORD)
            self.page.get_by_test_id("setup-confirm-password").fill(AUDIT_PASSWORD)
            setup_code = self.page.get_by_test_id("setup-code")
            if setup_code.count():
                self.visible(
                    setup_code, "setup code field is visible for this non-loopback peer"
                )
                self.require(
                    bool(DOCKER_CONTAINER),
                    "the target asked for a setup code but WEIR_LIVE_E2E_DOCKER_CONTAINER is not set",
                )
                setup_code.fill(read_docker_setup_code(DOCKER_CONTAINER))
            self.click(
                self.page.get_by_test_id("setup-submit"), "first-time setup submit"
            )
            # Bootstrap signs the new admin in directly (#704): the browser briefly lands on "/"
            # before RequireSetupWizard's own client-side redirect to the wizard settles, so a
            # visible element - the wizard or the already-signed-in shell, whichever this install
            # ends up on - is what to wait for, not a URL string that can be read mid-redirect.
            self.visible(wizard_or_shell, "setup wizard or the signed-in shell after bootstrap")

        login_user = self.page.get_by_test_id("login-username")
        if login_user.count():
            self.visible(login_user, "login form is visible")
            login_user.fill(AUDIT_USER)
            self.page.get_by_test_id("login-password").fill(AUDIT_PASSWORD)
            self.click(self.page.get_by_test_id("login-submit"), "login submit")
            self.visible(wizard_or_shell, "setup wizard or the signed-in shell after login")

        if wizard_skip.count() and wizard_skip.is_visible():
            self.click(
                wizard_skip, "skip setup wizard after exercising its entry path"
            )

        self.visible(shell_ready, "authenticated application shell")
        self.record("bootstrap, login, and setup-wizard state")

    def authenticated_read_surface(self) -> None:
        """Touch every non-streaming documented GET route without mutating data."""
        paths = (
            "/api/v1/activity/recent?limit=5",
            "/api/v1/auth/bootstrap/status",
            "/api/v1/auth/csrf",
            "/api/v1/auth/me",
            "/api/v1/auth/session",
            "/api/v1/auth/sessions",
            "/api/v1/media-managers/capabilities",
            "/api/v1/media-managers/connections",
            "/api/v1/media-managers/connections/999999",
            "/api/v1/pause",
            "/api/v1/processing/files?limit=5",
            "/api/v1/processing/files/999999/log",
            "/api/v1/processing/files/999999/log/download",
            "/api/v1/processing/files/999999/why-held",
            "/api/v1/processing/hardware",
            "/api/v1/processing/jobs/inspection?limit=5",
            "/api/v1/processing/libraries",
            "/api/v1/processing/libraries/999999",
            "/api/v1/processing/libraries/discover/999999",
            "/api/v1/processing/libraries/discover/999999/drift",
            "/api/v1/processing/maintenance",
            "/api/v1/processing/metadata-provider",
            "/api/v1/processing/operator-settings",
            "/api/v1/processing/overview-stats",
            "/api/v1/processing/rule-sets",
            "/api/v1/processing/runtime-settings",
            "/api/v1/suite/configuration-backups",
            "/api/v1/suite/configuration-backups/999999/download",
            "/api/v1/suite/configuration-bundle",
            "/api/v1/suite/logs?limit=5",
            "/api/v1/suite/metrics",
            "/api/v1/suite/notification-channels",
            "/api/v1/suite/security-overview",
            "/api/v1/suite/settings",
            "/api/v1/suite/update-settings",
            "/api/v1/suite/update-state",
            "/api/v1/suite/update-status",
            "/api/v1/system/directories",
            "/api/v1/system/media-tools",
            "/api/v1/system/readiness",
            "/api/v1/system/reconciliation",
        )
        headers = {
            "Accept": "application/json",
            "X-Requested-With": "XMLHttpRequest",
        }
        for path in paths:
            # Run protected reads in the signed-in page itself.  Playwright's
            # separate APIRequestContext does not reliably inherit the browser
            # session after the first-user bootstrap redirect on packaged
            # Windows builds, which made this audit report a false 401 even
            # while the authenticated shell was visibly loaded.
            expected_not_found = (
                "999999" in path or "00000000-0000-4000-8000-000000000000" in path
            )
            if expected_not_found:
                self.expected_not_found_urls.add(
                    urljoin(BASE_URL + "/", path.lstrip("/"))
                )
            self.expected_not_found_in_flight = expected_not_found
            try:
                status = self.page.evaluate(
                    """
                    async ({ path, headers }) => {
                      const response = await fetch(path, {
                        method: "GET",
                        credentials: "same-origin",
                        headers,
                      });
                      await response.arrayBuffer();
                      return response.status;
                    }
                    """,
                    {"path": path, "headers": headers},
                )
            finally:
                self.expected_not_found_in_flight = False
            self.require(
                status in {200, 204, 404},
                f"GET {path} returned unexpected HTTP {status}",
            )
            self.http_checks.append(f"GET {path} -> {status}")
        # The SSE route is intentionally excluded from the finite request
        # loop; the Activity browser step opens and closes it as a real client.
        self.record(
            f"authenticated read-only API surface ({len(paths)} routes; Activity SSE exercised in browser)"
        )
