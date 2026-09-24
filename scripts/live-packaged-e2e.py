"""End-to-end audit for a packaged Weir server.

This is intentionally separate from the developer E2E fixture.  It does not
start a source-tree server or Vite; it drives the browser against the URL
provided in ``WEIR_LIVE_BASE_URL``.  The target must be a controlled
packaged instance with a deliberate data/runtime boundary.

Usage (from the repository root)::

    python -m pip install --require-hashes -r tests/requirements.txt
    WEIR_LIVE_BASE_URL=http://app-server:8791 python scripts/live-packaged-e2e.py

The audit covers the authenticated routes, every Settings and System tab,
the safe CRUD/test controls, responsive shell controls, and the public HTTP
contract.  Screenshots and a machine-readable summary are written under the
ignored ``artifacts/live-packaged-e2e`` directory.

Against a Docker target, the container sees the audit's browser as the bridge
gateway rather than loopback, so first-run bootstrap asks for a setup code.
Set ``WEIR_LIVE_E2E_DOCKER_CONTAINER`` to the running container's name and
the audit reads the code the way a remote operator would: from ``docker
logs``, or the ``setup-code`` file under ``WEIR_LIVE_E2E_DOCKER_HOME``
(default ``/data/weir``) if the log has rotated past it.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import sys
import time
import uuid
from pathlib import Path
from typing import Any
from urllib.parse import urljoin

from playwright.sync_api import (
    BrowserContext,
    Locator,
    Page,
    Playwright,
    Response,
    sync_playwright,
)
from playwright.sync_api import (
    TimeoutError as PlaywrightTimeoutError,
)

BASE_URL = os.environ.get("WEIR_LIVE_BASE_URL", "").strip().rstrip("/")
AUDIT_USER = os.environ.get("WEIR_LIVE_E2E_USER", "live-audit-admin").strip()
AUDIT_PASSWORD = os.environ.get(
    "WEIR_LIVE_E2E_PASSWORD", "live-audit-pass-20260831"
)
ARTIFACT_DIR = Path(
    os.environ.get("WEIR_LIVE_E2E_ARTIFACTS", "artifacts/live-packaged-e2e")
)
FIXTURE_HOST_ROOT_RAW = os.environ.get(
    "WEIR_LIVE_E2E_FIXTURE_HOST_ROOT", ""
).strip()
FIXTURE_SERVER_ROOT = os.environ.get(
    "WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT", ""
).strip()
FIXTURE_FFMPEG = os.environ.get("WEIR_LIVE_E2E_FFMPEG", "ffmpeg").strip()
# The candidate's peer, seen from inside a Docker container, is the bridge gateway rather than
# loopback, so bootstrap needs the one-time setup code the same way a remote operator would get it.
# Unset for a target the audit reaches over real loopback (the developer fixture, a Windows smoke).
DOCKER_CONTAINER = os.environ.get("WEIR_LIVE_E2E_DOCKER_CONTAINER", "").strip()
DOCKER_WEIR_HOME = os.environ.get("WEIR_LIVE_E2E_DOCKER_HOME", "/data/weir").strip()
TIMEOUT_MS = 30_000


def project_version() -> str:
    """Resolve the expected packaged version without hard-coding a release."""

    explicit = os.environ.get("WEIR_LIVE_EXPECTED_VERSION", "").strip()
    if explicit:
        return explicit
    props = Path(__file__).resolve().parents[1] / "apps/server/Directory.Build.props"
    match = re.search(r"<WeirVersion>([^<]+)</WeirVersion>", props.read_text(encoding="utf-8"))
    if match is None:
        raise RuntimeError(f"WeirVersion was not found in {props}")
    return match.group(1).strip()


EXPECTED_VERSION = project_version()

SETUP_CODE_LOG_PATTERN = re.compile(r"enter this setup code:\s*([A-Z0-9]{4}-[A-Z0-9]{4})")


def read_docker_setup_code(container: str) -> str:
    """Reads the one-time setup code the way a remote operator is told to: from the container's
    own log line, or the setup-code file in its data folder if the log has since rotated past it."""

    logs = subprocess.run(
        ["docker", "logs", container],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    match = SETUP_CODE_LOG_PATTERN.search(logs.stdout + logs.stderr)
    if match:
        return match.group(1)

    cat = subprocess.run(
        ["docker", "exec", container, "cat", f"{DOCKER_WEIR_HOME}/setup-code"],
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    code = cat.stdout.strip()
    if not code:
        raise RuntimeError(
            f"Found no setup code in {container}'s log or {DOCKER_WEIR_HOME}/setup-code."
        )
    return code


class LiveAudit:
    """Small assertion/reporting wrapper used by the live browser audit."""

    def __init__(self, page: Page, context: BrowserContext) -> None:
        self.page = page
        self.context = context
        self.steps: list[str] = []
        self.screens: list[str] = []
        self.console_errors: list[str] = []
        self.console_warnings: list[str] = []
        self.page_errors: list[str] = []
        self.failed_requests: list[str] = []
        self.bad_responses: list[str] = []
        self.http_checks: list[str] = []
        self.completed_requests: set[tuple[str, str]] = set()
        self.expected_not_found_urls: set[str] = set()
        self.expected_not_found_in_flight = False
        self._attach_diagnostics()

    def _attach_diagnostics(self) -> None:
        def on_console(message: Any) -> None:
            text = (message.text or "").strip()
            if not text:
                return
            # The authenticated shell probes /auth/me before it knows whether
            # a session exists. Chromium reports that expected 401 as a
            # console error even though the response is part of the normal
            # anonymous bootstrap path.
            if (
                text
                == "Failed to load resource: the server responded with a status of 401 (Unauthorized)"
            ):
                return
            if (
                self.expected_not_found_in_flight
                and text
                == "Failed to load resource: the server responded with a status of 404 (Not Found)"
            ):
                return
            if message.type == "error":
                self.console_errors.append(text)
            elif message.type == "warning":
                self.console_warnings.append(text)

        def on_page_error(error: Any) -> None:
            self.page_errors.append(str(error))

        def on_request_failed(request: Any) -> None:
            failure = request.failure
            detail = failure if isinstance(failure, str) else "unknown failure"
            if (
                request.method,
                request.url,
            ) in self.completed_requests and detail == "net::ERR_ABORTED":
                return
            # EventSource is deliberately closed when Activity unmounts or a
            # filter/navigation replaces the stream. Chromium reports that
            # normal client-side close as ERR_ABORTED.
            if (
                request.url.endswith("/api/v1/activity/stream")
                and detail == "net::ERR_ABORTED"
            ):
                return
            self.failed_requests.append(f"{request.method} {request.url}: {detail}")

        def on_response(response: Response) -> None:
            if response.status < 400:
                self.completed_requests.add((response.request.method, response.url))
            if response.status >= 400:
                if response.status == 401 and response.url.rstrip("/").endswith(
                    "/api/v1/auth/me"
                ):
                    return
                if (
                    response.status == 404
                    and response.url in self.expected_not_found_urls
                ):
                    return
                self.bad_responses.append(f"{response.status} {response.url}")

        self.page.on("console", on_console)
        self.page.on("pageerror", on_page_error)
        self.page.on("requestfailed", on_request_failed)
        self.page.on("response", on_response)

    def record(self, message: str) -> None:
        self.steps.append(message)
        print(f"PASS  {message}")

    def require(self, condition: bool, message: str) -> None:
        if not condition:
            raise AssertionError(message)

    def visible(self, locator: Locator, message: str) -> Locator:
        try:
            locator.wait_for(state="visible", timeout=TIMEOUT_MS)
        except Exception:
            print(
                "DEBUG visible timeout: "
                f"{message}; url={self.page.url}; "
                f"body={self.page.locator('body').inner_text()[:1200]!r}; "
                f"console_errors={self.console_errors[:3]!r}; "
                f"page_errors={self.page_errors[:3]!r}; "
                f"failed_requests={self.failed_requests[:3]!r}",
                file=sys.stderr,
            )
            raise
        self.require(locator.is_visible(), message)
        return locator

    def click(self, locator: Locator, message: str) -> None:
        self.visible(locator, message).click()
        self.page.wait_for_timeout(120)

    def confirm_removal(self, test_id: str, names: str, message: str) -> None:
        """Answer the confirmation step now sitting in front of a Remove button.

        Remove no longer deletes on the first click. The dialog has to name the thing it
        is about to delete, which is the whole point of it, so that is asserted here
        rather than assumed.
        """

        dialog = self.visible(self.page.get_by_test_id(test_id), f"{message} dialog")
        self.require(
            f"Remove {names}?" in dialog.inner_text(),
            f"{message} dialog did not name {names}",
        )
        self.click(dialog.get_by_test_id(f"{test_id}-confirm"), message)

    def settle(self, timeout_ms: int = 1_500) -> None:
        try:
            self.page.wait_for_load_state("networkidle", timeout=timeout_ms)
        except PlaywrightTimeoutError:
            # Live polling pages intentionally stay busy.  The explicit waits
            # on the next screen are the meaningful synchronization points.
            pass

    def screenshot(self, name: str) -> None:
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        path = ARTIFACT_DIR / f"{name}.png"
        self.page.screenshot(path=str(path), full_page=True)
        self.screens.append(str(path))
        self.record(f"screenshot captured: {name}")

    def get(self, path: str, *, headers: dict[str, str] | None = None) -> Any:
        response = self.context.request.get(
            urljoin(BASE_URL + "/", path.lstrip("/")), headers=headers or {}
        )
        self.require(response.ok, f"GET {path} returned HTTP {response.status}")
        self.http_checks.append(f"GET {path} -> {response.status}")
        return response

    def browser_api(
        self,
        method: str,
        path: str,
        body: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        """Call a same-origin API through the signed-in browser session."""

        result = self.page.evaluate(
            """
            async ({ method, path, body }) => {
              const response = await fetch(path, {
                method,
                credentials: "same-origin",
                headers: {
                  "Accept": "application/json",
                  "Content-Type": "application/json",
                  "X-Requested-With": "XMLHttpRequest",
                },
                body: body === null ? undefined : JSON.stringify(body),
              });
              const text = await response.text();
              let payload = null;
              if (text) {
                try { payload = JSON.parse(text); }
                catch { payload = { raw: text }; }
              }
              return { status: response.status, payload };
            }
            """,
            {"method": method, "path": path, "body": body},
        )
        self.http_checks.append(f"{method} {path} -> {result['status']}")
        return result

    def csrf_token(self) -> str:
        result = self.browser_api("GET", "/api/v1/auth/csrf")
        self.require(result["status"] == 200, "could not obtain a CSRF token")
        token = str((result.get("payload") or {}).get("csrf_token") or "")
        self.require(bool(token), "CSRF response did not include a token")
        return token

    def assert_common_headers(self, response: Any, path: str) -> None:
        headers = {key.lower(): value for key, value in response.headers.items()}
        self.require("server" not in headers, f"{path} exposes a Server header")
        self.require(
            headers.get("cache-control", "").lower().startswith("no-store"),
            f"{path} is not marked no-store",
        )

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

    def open_sidebar(self, label: str) -> None:
        link = self.page.get_by_role("link", name=label, exact=True)
        self.click(link, f"open {label} from primary navigation")

    def assert_no_visible_crash(self) -> None:
        boundary = self.page.get_by_test_id("error-boundary")
        if boundary.count():
            self.require(not boundary.is_visible(), "error boundary is visible")
        self.require(
            not self.page.get_by_text("Something went wrong", exact=False).count(),
            "generic error state is visible",
        )

    def shell_and_responsive(self) -> None:
        self.page.set_viewport_size({"width": 1_440, "height": 1_000})
        self.page.goto(BASE_URL + "/", wait_until="domcontentloaded")
        self.visible(self.page.get_by_test_id("shell-ready"), "desktop shell")
        collapse = self.page.get_by_test_id("sidebar-collapse")
        self.require(
            collapse.get_attribute("aria-expanded") == "true", "sidebar starts expanded"
        )
        # The mark, not its box: the wordmark beside it folds away on collapse by design.
        logo = self.page.locator(".mm-sidebar .mm-logo-mark")
        expanded_logo = logo.bounding_box()
        self.click(collapse, "collapse sidebar")
        self.require(
            collapse.get_attribute("aria-expanded") == "false", "sidebar collapses"
        )
        self.page.wait_for_timeout(600)
        collapsed_logo = logo.bounding_box()
        self.require(
            expanded_logo is not None
            and collapsed_logo is not None
            and all(
                abs(expanded_logo[key] - collapsed_logo[key]) <= 1
                for key in ("x", "y", "width", "height")
            ),
            f"the logo moved or resized when the sidebar collapsed: {expanded_logo} -> {collapsed_logo}",
        )
        self.click(collapse, "expand sidebar")
        self.require(
            collapse.get_attribute("aria-expanded") == "true", "sidebar expands"
        )

        theme = self.page.get_by_test_id("theme-toggle")
        before = self.page.locator("html").get_attribute("data-mm-theme")
        self.click(theme, "toggle application theme")
        after = self.page.locator("html").get_attribute("data-mm-theme")
        self.require(
            after in {"dark", "light"} and after != before, "theme toggle did not apply"
        )
        self.click(theme, "toggle application theme back")

        self.page.set_viewport_size({"width": 390, "height": 844})
        self.page.goto(BASE_URL + "/", wait_until="domcontentloaded")
        menu = self.page.get_by_test_id("shell-nav-toggle")
        self.click(menu, "open mobile navigation")
        self.visible(
            self.page.get_by_role("button", name="Close navigation"),
            "mobile navigation backdrop",
        )
        self.click(
            self.page.get_by_role("button", name="Close navigation"),
            "close mobile navigation",
        )
        self.require(
            not self.page.get_by_role("button", name="Close navigation").count(),
            "mobile navigation did not close",
        )
        self.page.set_viewport_size({"width": 1_440, "height": 1_000})
        self.record("desktop collapse/theme and mobile navigation controls")

    def open_tab(self, sidebar: str, tab: str) -> None:
        """A tab on Settings or System (3.2): the side menu entry, then the tab across the top."""

        self.open_sidebar(sidebar)
        self.click(
            self.page.get_by_role("tab", name=tab, exact=True),
            f"open {sidebar} › {tab}",
        )
        self.visible(
            self.page.get_by_role("tab", name=tab, exact=True, selected=True),
            f"{sidebar} › {tab} selected",
        )

    def open_logs(self, show: str = "Events") -> None:
        """System › Logs, Weir's own events; ``show`` is the label of one option in its Show choice."""

        self.open_tab("System", "Logs")
        choice = self.visible(
            self.page.get_by_test_id("settings-history-show"), "Logs Show choice"
        )
        if show != "Events":
            choice.select_option(label=show)
        self.require(
            (choice.locator("option:checked").text_content() or "").strip() == show,
            f"Logs is not showing {show}",
        )

    def tab_labels(self, tabs_test_id: str) -> list[str]:
        tabs = self.page.get_by_test_id(tabs_test_id).get_by_role("tab")
        return [label.strip() for label in tabs.all_text_contents()]

    def processing_live(self) -> None:
        self.open_sidebar("Processing")
        self.visible(self.page.get_by_test_id("processing-page"), "Processing page")
        self.visible(
            self.page.get_by_role("heading", name="Processing", exact=True),
            "Processing heading",
        )
        # Five places since 3.2. Home became Processing, the dashboard folded into it (#459), every
        # file's story is History, and the 3.1 Activity page is System › Logs.
        primary = self.page.get_by_role("navigation", name="Primary")
        labels = [text.strip() for text in primary.locator(".mm-sidebar-link-label").all_text_contents()]
        self.require(
            labels == ["Processing", "History", "Library", "Settings", "System"],
            f"primary navigation is {labels}",
        )
        for retired in ("Home", "Dashboard", "Activity"):
            self.require(
                not self.page.get_by_role("link", name=retired, exact=True).count(),
                f"{retired} must not appear in the sidebar",
            )
        self.assert_no_visible_crash()
        self.screenshot("processing")
        self.record("Processing main screen and the five-place side menu")

    def library(self) -> None:
        self.open_sidebar("Library")
        self.visible(self.page.get_by_test_id("library-page"), "Library page")
        self.assert_no_visible_crash()
        self.screenshot("library")
        self.record("Library screen")

    def history_activity(self) -> None:
        self.open_logs()
        self.visible(self.page.get_by_test_id("activity-feed"), "Activity feed")
        # Scoped to the filters: the Show choice above them is a select too.
        filters = self.visible(
            self.page.get_by_test_id("activity-filters"), "Activity filters"
        )
        selects = filters.locator("select")
        self.require(selects.count() >= 2, "Activity filters are incomplete")
        self.require(
            self.page.get_by_text("All modules", exact=True).count() == 0,
            "Activity still shows a Module filter",
        )
        if selects.nth(0).locator("option").count() > 1:
            selects.nth(0).select_option(index=1)
        filters.get_by_placeholder("Search titles and details").fill("audit")
        filters.locator('input[type="datetime-local"]').nth(0).fill("2026-01-01T00:00")
        filters.locator('input[type="datetime-local"]').nth(1).fill("2026-12-31T23:59")
        self.click(
            filters.get_by_role("button", name="Apply filters", exact=True),
            "apply Activity filters",
        )
        self.visible(
            self.page.get_by_test_id("activity-summary").get_by_text("matching your filters"),
            "Activity active filter state",
        )
        # Exactly "Clear →": its neighbour "Clear all history →" deletes Activity.
        self.click(
            filters.get_by_role("button", name="Clear →", exact=True),
            "clear Activity filters",
        )
        self.page.wait_for_timeout(500)
        self.require(
            not self.page.get_by_test_id("activity-summary").get_by_text("matching your filters").count(),
            "Activity filters did not clear",
        )
        self.screenshot("history-activity")
        self.record("Logs: Weir's own events, filters, and clear action")

    def settings_tabs(self) -> None:
        self.open_sidebar("Settings")
        self.visible(self.page.get_by_test_id("suite-settings-page"), "Settings page")
        expected = {
            "Libraries": "processing-libraries-section",
            "Rules": "processing-rule-set-workspace",
            "Media managers": "suite-settings-media-managers",
            "Performance": "processing-direct-play-section",
            "Cleanup": "processing-maintenance-section",
            "Schedule": "processing-schedules-section",
            "Alerts": "suite-settings-notifications",
        }
        labels = self.tab_labels("settings-section-tabs")
        self.require(labels == list(expected), f"Settings tabs are {labels}")
        for tab, test_id in expected.items():
            self.click(
                self.page.get_by_role("tab", name=tab, exact=True),
                f"open Settings {tab} tab",
            )
            self.visible(self.page.get_by_test_id(test_id), f"Settings {tab} panel")

            if tab == "Libraries":
                edit_buttons = self.page.get_by_role("button", name="Edit", exact=True)
                if edit_buttons.count():
                    self.click(edit_buttons.first, "open library editor")
                    self.visible(
                        self.page.get_by_test_id("processing-library-form"),
                        "library form",
                    )
                    cancel = self.page.get_by_role("button", name="Cancel", exact=True)
                    if cancel.count():
                        self.click(cancel.last, "cancel library editor")
            elif tab == "Schedule":
                # A week per library, the time zone above them (canvas board 6).
                self.visible(
                    self.page.get_by_text("Time zone", exact=True),
                    "time zone control",
                )
                self.require(
                    self.page.get_by_test_id("schedule-library-row").count() > 0,
                    "no library weeks on Schedule",
                )

        self.screenshot("settings")
        self.record(
            "Settings libraries, rules, media managers, performance, cleanup, schedule, and alerts tabs"
        )

    def history_and_jobs(self) -> None:
        # History: every file Weir has touched, with the open file's record beside the list.
        self.open_sidebar("History")
        self.visible(self.page.get_by_test_id("history-page"), "History page")
        chips = self.page.get_by_role("group", name="Show").get_by_role("button")
        # Six groups since library cleans joined downloads in History (#695): a skip is its own
        # neutral group rather than counting as Failed, and on_hold sits under Needs you.
        expected_chip_labels = [
            "All",
            "In progress",
            "Finished",
            "Needs you",
            "Skipped",
            "Failed",
        ]
        chip_texts = chips.all_inner_texts()
        self.require(
            len(chip_texts) == len(expected_chip_labels)
            and all(
                text.startswith(label)
                for text, label in zip(chip_texts, expected_chip_labels)
            ),
            f"History shows {chip_texts!r}, not {expected_chip_labels!r} in order",
        )
        # A fresh install has no files, so assert whichever of the two states is real, and never
        # that the page rendered nothing at all.
        if self.page.get_by_test_id("history-detail").count():
            self.visible(self.page.get_by_test_id("history-detail"), "History open file")
        else:
            self.require(
                self.page.get_by_text("Nothing yet.", exact=False).count() > 0
                or self.page.get_by_text("No file matches", exact=False).count() > 0,
                "History showed neither files nor its empty state",
            )
        search = self.page.get_by_role("searchbox", name="Find a file")
        search.fill("audit")
        search.press("Enter")
        self.visible(self.page.get_by_test_id("history-page"), "History after a search")
        self.screenshot("history")

        self.open_logs("Weir's jobs")
        self.visible(
            self.page.get_by_test_id("processing-jobs-inspection-section"),
            "Logs jobs list",
        )
        self.screenshot("logs-jobs")
        self.record("History: every file and its record; Logs: Weir's jobs")

    def system_instance_and_setup(self) -> None:
        self.open_sidebar("System")
        self.visible(self.page.get_by_test_id("suite-system-page"), "System page")
        labels = self.tab_labels("system-section-tabs")
        self.require(
            labels == ["About", "Backups", "Security", "Logs"],
            f"System tabs are {labels}",
        )
        self.visible(
            self.page.get_by_test_id("suite-settings-global"), "System › About"
        )
        self.require(
            self.page.get_by_text("What Weir works with", exact=True).count()
            > 0,
            "runtime facts are missing from About",
        )
        # Display density was removed in 3.2 and must not come back.
        self.require(
            not self.page.get_by_text("Display density", exact=False).count(),
            "Display density is back",
        )
        self.require(
            self.page.locator("html").get_attribute("data-mm-density") is None,
            "the page still carries a display density",
        )
        self.visible(
            self.page.get_by_test_id("suite-settings-upgrade-tab"), "Upgrade section"
        )
        self.click(
            self.page.get_by_role("button", name="Check again →", exact=True),
            "refresh upgrade status",
        )

        # Exercise the wizard's supported re-entry path, then leave it with the safe skip action so
        # the disposable audit account remains usable. It folds away because it is run once.
        self.click(
            self.page.get_by_role("heading", name="Setup wizard", exact=True),
            "open the Setup wizard group",
        )
        self.click(
            self.page.get_by_test_id("suite-settings-open-setup-wizard"),
            "open setup wizard from System",
        )
        self.visible(
            self.page.get_by_test_id("setup-wizard-skip"), "re-entered setup wizard"
        )
        self.require(
            not self.page.get_by_text("Display density", exact=False).count(),
            "Display density is back in the setup wizard",
        )
        self.click(
            self.page.get_by_test_id("setup-wizard-skip"),
            "skip re-entered setup wizard",
        )
        self.page.wait_for_url(
            lambda url: "/setup-wizard" not in url, timeout=TIMEOUT_MS
        )
        self.open_sidebar("System")
        self.visible(
            self.page.get_by_test_id("suite-settings-global"),
            "return to System › About",
        )
        self.screenshot("system-instance")
        self.record("System › About: time zone, runtime facts, upgrade refresh, and wizard re-entry")

    def system_backups_logs_security(self) -> None:
        self.open_tab("System", "Backups")
        self.visible(
            self.page.get_by_test_id("suite-settings-backup-tab"),
            "System backups panel",
        )
        self.require(
            self.page.get_by_role(
                "button", name="Download configuration now", exact=True
            ).count()
            > 0,
            "configuration download control missing",
        )
        with self.page.expect_download(timeout=TIMEOUT_MS) as download_info:
            self.page.get_by_role(
                "button", name="Download configuration now", exact=True
            ).click()
        self.require(
            download_info.value.suggested_filename.endswith(".json"),
            "configuration export is not JSON",
        )

        self.open_logs("Server log")
        logs = self.visible(
            self.page.get_by_test_id("suite-settings-logs"), "server log panel"
        )
        self.visible(
            logs.get_by_text("Server diagnostics", exact=True),
            "server diagnostics disclosure",
        )
        logs.get_by_placeholder(
            "Search message, detail, traceback, logger, or source"
        ).fill("audit")
        # Scoped to the log panel: the Show choice above it is a select too.
        level_select = logs.locator("select").first
        if level_select.count():
            level_select.select_option(index=1)
        toggles = logs.get_by_role("radio")
        if toggles.count() >= 2:
            toggles.last.click()
        refresh = logs.get_by_role("button", name="Refresh →", exact=True)
        if refresh.count():
            self.click(refresh, "refresh server log")

        self.open_tab("System", "Security")
        self.visible(
            self.page.get_by_test_id("suite-settings-security"),
            "System security panel",
        )
        self.visible(
            self.page.get_by_text("How sign-in is protected", exact=True), "how sign-in is protected"
        )
        self.visible(
            self.page.get_by_text("Active sessions", exact=True), "active sessions"
        )
        self.visible(
            self.page.get_by_role("heading", name="Change password", exact=True),
            "change-password controls",
        )
        self.screenshot("system-security")
        self.record(
            "System backup/export, server log filters, and security/session posture"
        )

    def settings_notifications(self) -> None:
        self.open_tab("Settings", "Alerts")
        self.visible(
            self.page.get_by_test_id("suite-settings-notifications"),
            "Settings alerts panel",
        )
        # Make reruns safe after a diagnostic failure leaves the disposable
        # channel behind.
        existing = self.page.get_by_text("Live audit channel", exact=True)
        while existing.count():
            card = existing.first.locator("xpath=ancestor::tr")
            self.click(
                card.get_by_role("button", name="Remove", exact=True),
                "ask to remove leftover notification channel",
            )
            with self.page.expect_response(
                lambda response: (
                    response.request.method == "DELETE"
                    and "/api/v1/suite/notification-channels/" in response.url
                ),
                timeout=TIMEOUT_MS,
            ) as delete_response:
                self.confirm_removal(
                    "notification-channel-remove-confirm",
                    "Live audit channel",
                    "remove leftover notification channel",
                )
            self.require(
                delete_response.value.status == 204,
                "leftover notification channel removal failed",
            )
            existing.first.wait_for(state="detached", timeout=TIMEOUT_MS)
        self.click(
            self.page.get_by_role(
                "button", name="Add an alert →", exact=True
            ),
            "open notification channel form",
        )
        form = (
            self.page.locator("form")
            .filter(has=self.page.get_by_text("Label", exact=True))
            .last
        )
        self.visible(form, "notification channel form")
        labels = form.locator("input[type='text']")
        self.require(labels.count() >= 1, "notification label control missing")
        labels.first.fill("Live audit channel")
        form.locator("input[type='url']").fill("https://example.invalid/webhook")
        events = form.locator("input[type='checkbox']")
        self.require(events.count() > 0, "notification event controls missing")
        # Keep the default failure event selected and exercise the enabled switch
        # without leaving the final channel disabled.
        self.click(
            form.get_by_role("button", name="Save alert", exact=True),
            "create notification channel",
        )
        row = self.page.get_by_text("Live audit channel", exact=True)
        self.visible(row, "created notification channel")
        row_parent = row.locator("xpath=ancestor::tr")
        self.click(
            row_parent.get_by_role("button", name="Edit", exact=True),
            "edit notification channel",
        )
        self.visible(
            self.page.get_by_text("Edit alert", exact=True), "notification edit form"
        )
        self.click(
            self.page.get_by_role("button", name="Cancel", exact=True),
            "cancel notification edit",
        )
        row_parent = self.page.get_by_text("Live audit channel", exact=True).locator(
            "xpath=ancestor::tr"
        )
        # Cancelling has to leave the channel exactly where it was: that is the promise the
        # confirmation makes, and it is worth proving before relying on the confirm path.
        self.click(
            row_parent.get_by_role("button", name="Remove", exact=True),
            "ask to remove notification channel",
        )
        self.click(
            self.page.get_by_test_id("notification-channel-remove-confirm-cancel"),
            "cancel notification channel removal",
        )
        self.visible(
            self.page.get_by_text("Live audit channel", exact=True),
            "notification channel survived a cancelled removal",
        )
        row_parent = self.page.get_by_text("Live audit channel", exact=True).locator(
            "xpath=ancestor::tr"
        )
        # Escape is the other way out, and it must not delete either.
        self.click(
            row_parent.get_by_role("button", name="Remove", exact=True),
            "ask to remove notification channel again",
        )
        self.visible(
            self.page.get_by_test_id("notification-channel-remove-confirm"),
            "notification channel removal confirmation",
        )
        self.page.keyboard.press("Escape")
        self.page.wait_for_timeout(120)
        self.visible(
            self.page.get_by_text("Live audit channel", exact=True),
            "notification channel survived Escape",
        )
        row_parent = self.page.get_by_text("Live audit channel", exact=True).locator(
            "xpath=ancestor::tr"
        )
        self.click(
            row_parent.get_by_role("button", name="Remove", exact=True),
            "ask to remove notification channel once more",
        )
        with self.page.expect_response(
            lambda response: (
                response.request.method == "DELETE"
                and "/api/v1/suite/notification-channels/" in response.url
            ),
            timeout=TIMEOUT_MS,
        ) as delete_response:
            self.confirm_removal(
                "notification-channel-remove-confirm",
                "Live audit channel",
                "remove notification channel",
            )
        self.require(
            delete_response.value.status == 204, "notification channel removal failed"
        )
        self.page.get_by_text("Live audit channel", exact=True).wait_for(
            state="detached", timeout=TIMEOUT_MS
        )
        self.record("notification channel create, edit/cancel, and confirmed remove")

    def settings_media_managers(self) -> None:
        self.click(
            self.page.get_by_role("tab", name="Media managers", exact=True),
            "open Settings media managers",
        )
        self.visible(
            self.page.get_by_test_id("media-manager-add"),
            "Settings media managers panel",
        )
        for card in self.page.get_by_test_id("media-manager-card").all():
            if "Live audit manager" in card.inner_text():
                self.click(
                    card.get_by_test_id("media-manager-remove"),
                    "ask to remove leftover media manager",
                )
                with self.page.expect_response(
                    lambda response: (
                        response.request.method == "DELETE"
                        and "/api/v1/media-managers/connections/" in response.url
                    ),
                    timeout=TIMEOUT_MS,
                ) as delete_response:
                    self.confirm_removal(
                        "media-manager-remove-confirm",
                        "Live audit manager",
                        "remove leftover media manager",
                    )
                self.require(
                    delete_response.value.status == 204,
                    "leftover media manager removal failed",
                )
                card.wait_for(state="detached", timeout=TIMEOUT_MS)
        self.click(
            self.page.get_by_test_id("media-manager-add"), "open media manager form"
        )
        self.page.get_by_test_id("media-manager-name").fill("Live audit manager")
        self.page.get_by_test_id("media-manager-base-url").fill("http://127.0.0.1:9")
        self.page.get_by_test_id("media-manager-api-key").fill("audit-secret")
        self.click(
            self.page.get_by_test_id("media-manager-save"), "create media manager"
        )
        card = self.page.get_by_test_id("media-manager-card").filter(
            has_text="Live audit manager"
        )
        self.visible(card, "created media manager")
        self.visible(
            card.get_by_test_id("media-manager-status"), "media manager status"
        )
        self.click(
            card.get_by_test_id("media-manager-setup-details").locator("summary"),
            "open media manager setup details",
        )
        self.click(
            card.get_by_test_id("media-manager-generate-secret"),
            "generate media manager webhook secret",
        )
        self.visible(
            card.get_by_test_id("media-manager-secret"), "generated webhook secret"
        )
        self.click(
            card.get_by_role("button", name="Disable", exact=True),
            "disable media manager",
        )
        self.click(
            card.get_by_role("button", name="Enable", exact=True),
            "enable media manager",
        )
        with self.page.expect_response(
            lambda response: (
                response.request.method == "POST"
                and "/api/v1/media-managers/connections/" in response.url
                and response.url.endswith("/test")
            ),
            timeout=TIMEOUT_MS,
        ) as test_response:
            self.click(
                card.get_by_test_id("media-manager-test"),
                "test media manager connection",
            )
        self.require(
            test_response.value.status == 200,
            "media manager connection failure was not returned as a normal test result",
        )
        self.visible(
            card.get_by_test_id("media-manager-status"), "media manager test result"
        )
        # Cancelling must leave the connection intact - the confirmation is only worth
        # anything if "no" really means nothing happened.
        self.click(
            card.get_by_test_id("media-manager-remove"),
            "ask to remove media manager",
        )
        self.click(
            self.page.get_by_test_id("media-manager-remove-confirm-cancel"),
            "cancel media manager removal",
        )
        self.visible(card, "media manager survived a cancelled removal")
        self.click(
            card.get_by_test_id("media-manager-remove"),
            "ask to remove media manager again",
        )
        with self.page.expect_response(
            lambda response: (
                response.request.method == "DELETE"
                and "/api/v1/media-managers/connections/" in response.url
            ),
            timeout=TIMEOUT_MS,
        ) as delete_response:
            self.confirm_removal(
                "media-manager-remove-confirm",
                "Live audit manager",
                "remove media manager",
            )
        self.require(
            delete_response.value.status == 204, "media manager removal failed"
        )
        self.require(
            not self.page.get_by_test_id("media-manager-card")
            .filter(has_text="Live audit manager")
            .count(),
            "media manager was not removed",
        )
        self.screenshot("settings-integrations")
        self.record(
            "media-manager create, secret generation, enable/disable, connection test, and confirmed remove"
        )

    def processing_pass_through_lifecycle(self) -> None:
        """Prove an unchanged file reaches output before its watched source is removed."""

        configured = bool(FIXTURE_HOST_ROOT_RAW or FIXTURE_SERVER_ROOT)
        if not configured:
            self.record(
                "Processing pass-through lifecycle skipped (no controlled fixture mount)"
            )
            return
        self.require(
            bool(FIXTURE_HOST_ROOT_RAW and FIXTURE_SERVER_ROOT),
            "both Processing fixture host and server roots are required",
        )
        self.require(bool(FIXTURE_FFMPEG), "Processing fixture FFmpeg command is required")

        host_root = Path(FIXTURE_HOST_ROOT_RAW).expanduser().resolve()
        host_root.mkdir(parents=True, exist_ok=True)
        fixture_name = f"pass-through-{uuid.uuid4().hex}"
        fixture_root = host_root / fixture_name
        watch = fixture_root / "watch"
        work = fixture_root / "work"
        output = fixture_root / "processed"
        release = watch / "ForeignFilm"
        for directory in (fixture_root, watch, work, output, release):
            directory.mkdir(parents=True, exist_ok=False)
            if os.name != "nt":
                directory.chmod(0o777)

        source = release / "foreign-only.mkv"
        subprocess.run(
            [
                FIXTURE_FFMPEG,
                "-nostdin",
                "-hide_banner",
                "-loglevel",
                "error",
                "-f",
                "lavfi",
                "-i",
                "color=c=black:s=320x180:d=2",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=440:duration=2",
                "-map",
                "0:v",
                "-map",
                "1:a",
                "-c:v",
                "mpeg4",
                "-c:a",
                "aac",
                "-metadata:s:a:0",
                "language=jpn",
                "-y",
                str(source),
            ],
            check=True,
            timeout=60,
        )
        self.require(source.is_file(), "FFmpeg did not create the pass-through fixture")
        old_time = time.time() - 600
        os.utime(source, (old_time, old_time))
        source_size = source.stat().st_size
        source_hash = hashlib.sha256(source.read_bytes()).hexdigest()

        server_separator = (
            "\\" if re.match(r"^[A-Za-z]:[\\/]", FIXTURE_SERVER_ROOT) else "/"
        )

        def server_path(*parts: str) -> str:
            root = FIXTURE_SERVER_ROOT.rstrip("/\\")
            return root + server_separator + server_separator.join(parts)

        # The seeded Movies library is configured directly; the path-settings route was retired in #460.
        libraries = self.browser_api("GET", "/api/v1/processing/libraries")
        movies = next(
            (row for row in (libraries.get("payload") or []) if row.get("media_type") == "movie"),
            None,
        )
        self.require(movies is not None, "the install has no Movies library to configure")
        path_result = self.browser_api(
            "PUT",
            f"/api/v1/processing/libraries/{movies['id']}",
            {
                "csrf_token": self.csrf_token(),
                "name": movies["name"],
                "media_type": "movie",
                "watched_folder": server_path(fixture_name, "watch"),
                "work_folder": server_path(fixture_name, "work"),
                "output_folder": server_path(fixture_name, "processed"),
                "manager_connection_ids": movies.get("manager_connection_ids") or [],
            },
        )
        self.require(
            path_result["status"] == 200,
            f"could not configure pass-through fixture paths: {path_result['payload']}",
        )
        enqueue = self.browser_api(
            "POST",
            "/api/v1/processing/jobs/file-remux-pass/enqueue",
            {
                "csrf_token": self.csrf_token(),
                "relative_media_path": "ForeignFilm/foreign-only.mkv",
                "media_scope": "movie",
                "pass_through_unchanged": True,
            },
        )
        self.require(
            enqueue["status"] == 200,
            f"could not enqueue pass-through fixture: {enqueue['payload']}",
        )
        job_id = int((enqueue.get("payload") or {}).get("job_id") or 0)
        self.require(job_id > 0, "pass-through enqueue did not return a job id")

        delivered = output / "ForeignFilm" / "foreign-only.mkv"
        terminal_status = ""
        last_error = ""
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            inspection = self.browser_api(
                "GET", "/api/v1/processing/jobs/inspection?limit=100"
            )
            self.require(
                inspection["status"] == 200,
                "could not inspect the pass-through job",
            )
            rows = (inspection.get("payload") or {}).get("jobs") or []
            row = next((item for item in rows if item.get("id") == job_id), None)
            if row:
                terminal_status = str(row.get("status") or "")
                last_error = str(row.get("last_error") or "")
                if terminal_status in {"failed", "cancelled"}:
                    break
            if (
                terminal_status == "completed"
                and delivered.is_file()
                and not source.exists()
            ):
                break
            time.sleep(0.25)

        self.require(
            delivered.is_file(),
            f"pass-through output was not created (status={terminal_status}, error={last_error})",
        )
        self.require(not source.exists(), "pass-through source was not cleaned up")
        self.require(
            terminal_status == "completed",
            f"pass-through job did not complete (status={terminal_status}, error={last_error})",
        )
        output_size = delivered.stat().st_size
        output_hash = hashlib.sha256(delivered.read_bytes()).hexdigest()
        self.require(output_size == source_size, "pass-through output size changed")
        self.require(output_hash == source_hash, "pass-through output bytes changed")

        proof = {
            "job_id": job_id,
            "job_status": terminal_status,
            "source_removed": True,
            "output_created": True,
            "source_bytes": source_size,
            "output_bytes": output_size,
            "source_sha256": source_hash,
            "output_sha256": output_hash,
            "result": "passed",
        }
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        (ARTIFACT_DIR / "pass-through-proof.json").write_text(
            json.dumps(proof, indent=2), encoding="utf-8"
        )
        self.record(
            "Processing pass-through placed byte-identical output before cleaning the watched source"
        )

    def settings_history_and_navigation(self) -> None:
        for sidebar, first, first_id, second, second_param, second_id in (
            (
                "Settings",
                "Libraries",
                "processing-libraries-section",
                "Schedule",
                "tab=schedule",
                "processing-schedules-section",
            ),
            (
                "System",
                "About",
                "suite-settings-global",
                "Security",
                "tab=security",
                "suite-settings-security",
            ),
        ):
            self.open_tab(sidebar, first)
            self.visible(
                self.page.get_by_test_id(first_id), f"{sidebar} history origin {first}"
            )
            self.click(
                self.page.get_by_role("tab", name=second, exact=True),
                f"exercise {sidebar} URL history forward target",
            )
            self.require(
                second_param in self.page.url,
                f"{sidebar} {second} tab is not represented in the URL",
            )
            self.page.go_back()
            self.visible(
                self.page.get_by_test_id(first_id),
                f"{sidebar} browser-back returns {first}",
            )
            self.page.go_forward()
            self.visible(
                self.page.get_by_test_id(second_id),
                f"{sidebar} browser-forward returns {second}",
            )
        self.page.goto(BASE_URL + "/not-a-real-screen", wait_until="domcontentloaded")
        self.visible(
            self.page.get_by_text("This page doesn't exist.", exact=False),
            "not-found route",
        )
        self.record("Settings and System URL history and not-found route")

    def finish(self) -> dict[str, Any]:
        # Warnings are retained in the report for review.  Runtime errors,
        # failed requests, and HTTP errors are release blockers.
        self.require(
            not self.console_errors,
            f"browser console errors: {self.console_errors[:5]}",
        )
        self.require(
            not self.page_errors, f"browser page errors: {self.page_errors[:5]}"
        )
        self.require(
            not self.failed_requests,
            f"browser request failures: {self.failed_requests[:5]}",
        )
        self.require(
            not self.bad_responses, f"browser HTTP errors: {self.bad_responses[:10]}"
        )
        report = {
            "base_url": BASE_URL,
            "server_version": EXPECTED_VERSION,
            "steps": self.steps,
            "http_checks": self.http_checks,
            "screenshots": self.screens,
            "console_warnings": self.console_warnings,
            "console_errors": self.console_errors,
            "page_errors": self.page_errors,
            "failed_requests": self.failed_requests,
            "bad_responses": self.bad_responses,
        }
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        (ARTIFACT_DIR / "summary.json").write_text(
            json.dumps(report, indent=2), encoding="utf-8"
        )
        return report


def run(playwright: Playwright) -> dict[str, Any]:
    browser = playwright.chromium.launch(headless=True)
    context = browser.new_context(
        viewport={"width": 1_440, "height": 1_000}, ignore_https_errors=True
    )
    page = context.new_page()
    page.set_default_timeout(TIMEOUT_MS)
    audit = LiveAudit(page, context)
    try:
        audit.public_contract()
        audit.bootstrap_and_sign_in()
        audit.authenticated_read_surface()
        audit.shell_and_responsive()
        audit.processing_live()
        audit.library()
        audit.history_activity()
        audit.settings_tabs()
        audit.history_and_jobs()
        audit.system_instance_and_setup()
        audit.system_backups_logs_security()
        audit.settings_notifications()
        audit.settings_media_managers()
        audit.processing_pass_through_lifecycle()
        audit.settings_history_and_navigation()
        return audit.finish()
    except Exception:
        ARTIFACT_DIR.mkdir(parents=True, exist_ok=True)
        try:
            page.screenshot(path=str(ARTIFACT_DIR / "failure.png"), full_page=True)
        except Exception as screenshot_error:  # noqa: BLE001 - preserve the original audit failure
            print(
                f"WARN  could not capture failure screenshot: {screenshot_error}",
                file=sys.stderr,
            )
        raise
    finally:
        context.close()
        browser.close()


def main() -> int:
    if not BASE_URL:
        print("WEIR_LIVE_BASE_URL is required", file=sys.stderr)
        return 2
    print(f"Auditing packaged Weir at {BASE_URL}")
    try:
        with sync_playwright() as playwright:
            report = run(playwright)
    except Exception as exc:  # noqa: BLE001 - convert any audit failure into a non-zero CLI result
        print(f"FAIL  packaged live audit: {exc}", file=sys.stderr)
        return 1
    print(
        f"PASS  packaged live audit complete: {len(report['steps'])} steps, "
        f"{len(report['screenshots'])} screenshots, "
        f"{len(report['console_warnings'])} console warnings"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
