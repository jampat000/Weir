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

The audit itself (``LiveAudit`` below) is composed from the ``Audit*`` mixins in the
``live_packaged_e2e`` package next to this script, split out by screen/area (#747) so no one
file holds the whole thing. This script stays the only supported entry point.
"""

from __future__ import annotations

import sys
from typing import Any

from playwright.sync_api import Playwright, sync_playwright

from live_packaged_e2e.audit_core import AuditCore
from live_packaged_e2e.audit_notifications import AuditNotificationsMixin
from live_packaged_e2e.audit_processing import AuditProcessingMixin
from live_packaged_e2e.audit_public import AuditPublicMixin
from live_packaged_e2e.audit_settings import AuditSettingsMixin
from live_packaged_e2e.audit_shell import AuditShellMixin
from live_packaged_e2e.config import ARTIFACT_DIR, BASE_URL, TIMEOUT_MS


class LiveAudit(
    AuditCore,
    AuditPublicMixin,
    AuditShellMixin,
    AuditSettingsMixin,
    AuditNotificationsMixin,
    AuditProcessingMixin,
):
    """The full packaged-server audit: see the ``Audit*`` mixins for what each screen covers."""


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
