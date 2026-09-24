"""``AuditCore``: diagnostics collection, the small assertion/reporting helpers every audit step
builds on, and the final report. The other ``Audit*`` mixins in this package assume it runs first
in the MRO (``LiveAudit`` in ``scripts/live-packaged-e2e.py`` composes them all into one class).
"""

from __future__ import annotations

import json
import sys
from typing import Any
from urllib.parse import urljoin

from playwright.sync_api import BrowserContext, Locator, Page, Response
from playwright.sync_api import TimeoutError as PlaywrightTimeoutError

from .config import ARTIFACT_DIR, BASE_URL, EXPECTED_VERSION, TIMEOUT_MS


class AuditCore:
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
