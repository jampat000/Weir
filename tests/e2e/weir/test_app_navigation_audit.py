from __future__ import annotations

import os
import re

import pytest
from playwright.sync_api import expect, sync_playwright

from ._helpers import ensure_signed_in, open_history, open_sidebar, open_tab

pytestmark = [
    pytest.mark.weir_e2e,
    pytest.mark.skipif(
        os.environ.get("WEIR_E2E") != "1",
        reason="Weir E2E requires WEIR_E2E=1 (see tests/e2e/weir/conftest.py).",
    ),
]

# Settings tab -> what it shows (3.2). Settings is about your media; Weir itself is System.
SETTINGS_TABS = (
    ("Libraries", "processing-libraries-section"),
    ("Rules", "processing-rule-set-workspace"),
    ("Media managers", "suite-settings-media-managers"),
    ("Running", "processing-direct-play-section"),
    ("Housekeeping", "processing-maintenance-section"),
    ("Schedule", "processing-schedules-section"),
    ("Alerts", "suite-settings-notifications"),
)


def test_signed_in_navigation_covers_main_screens_and_tabs(weir_shell: str) -> None:
    base = weir_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page()
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)

            # Four places since 3.2: Processing (the landing screen), Library, Settings and System.
            primary = page.get_by_role("navigation", name="Primary")
            # The labels, not the links: the Processing link also carries its "1 working" badge while a file runs.
            expect(primary.locator(".mm-sidebar-link-label")).to_have_text(["Processing", "Library", "Settings", "System"])
            for retired in ("Home", "Dashboard", "Activity"):
                expect(page.get_by_role("link", name=retired, exact=True)).to_have_count(0)

            open_sidebar(page, "Processing")
            expect(page).to_have_url(re.compile(r".*/(?:$|[?#])"))
            expect(page.get_by_test_id("processing-page")).to_be_visible()
            expect(page.get_by_role("heading", name="Processing", exact=True)).to_be_visible()
            # Older than 3.1 gets the not-found page, not a hidden alias.
            page.goto(f"{base}/dashboard", wait_until="domcontentloaded")
            expect(page.get_by_role("heading", name="Page not found", exact=True)).to_be_visible()
            page.goto(base + "/", wait_until="domcontentloaded")
            expect(page.get_by_test_id("processing-page")).to_be_visible()

            open_sidebar(page, "Library")
            expect(page).to_have_url(re.compile(r".*/library(?:$|[?#])"))
            expect(page.get_by_test_id("library-page")).to_be_visible()

            open_sidebar(page, "Settings")
            expect(page).to_have_url(re.compile(r".*/settings(?:$|[?#])"))
            expect(page.get_by_test_id("suite-settings-page")).to_be_visible()
            settings_tabs = page.get_by_test_id("settings-section-tabs").get_by_role("tab")
            expect(settings_tabs).to_have_text([label for label, _ in SETTINGS_TABS])
            for label, section in SETTINGS_TABS:
                page.get_by_role("tab", name=label, exact=True).click()
                expect(page.get_by_test_id(section)).to_be_visible()

            open_sidebar(page, "System")
            expect(page).to_have_url(re.compile(r".*/system(?:$|[?#])"))
            expect(page.get_by_test_id("suite-system-page")).to_be_visible()
            expect(page.get_by_test_id("system-section-tabs").get_by_role("tab")).to_have_text(
                ["This instance", "Backups", "Security", "History and logs"]
            )
            expect(page.get_by_test_id("suite-settings-global")).to_be_visible()
            expect(page.get_by_text("Setup wizard", exact=True)).to_be_visible()
            expect(page.get_by_text("Time zone", exact=True)).to_be_visible()
            expect(page.get_by_text("Upgrade", exact=True)).to_be_visible()
            # Display density was removed in 3.2 and must not come back.
            expect(page.get_by_text("Display density", exact=False)).to_have_count(0)
            expect(page.locator("html")).not_to_have_attribute("data-mm-density", re.compile(".*"))

            open_tab(page, "System", "Security")
            expect(page.get_by_test_id("suite-settings-security")).to_be_visible()
            expect(page.get_by_role("heading", name="Change password", exact=True)).to_be_visible()

            open_history(page)
            expect(page).to_have_url(re.compile(r".*/system\?tab=history(?:$|[&#])"))
            expect(page.get_by_test_id("activity-feed")).to_be_visible()
            expect(page.get_by_test_id("activity-summary")).to_contain_text("Showing")
            # Weir is one app: no Module filter.
            expect(page.get_by_text("All modules", exact=True)).to_have_count(0)

            open_history(page, "Jobs")
            expect(page.get_by_test_id("processing-jobs-inspection-section")).to_be_visible()

            open_history(page, "Server log")
            expect(page.get_by_test_id("suite-settings-logs")).to_be_visible()
            expect(page.get_by_text("Showing now", exact=False)).to_be_visible()
            expect(page.get_by_text("Matching events", exact=False)).to_be_visible()
            expect(page.get_by_text("Server diagnostics", exact=True)).to_be_visible()
            expect(page.get_by_text("System events", exact=True)).to_be_visible()

            # 3.1 has been installed, so its addresses land on the same thing in its new place.
            page.goto(f"{base}/activity", wait_until="domcontentloaded")
            expect(page).to_have_url(re.compile(r".*/system\?tab=history$"))
            expect(page.get_by_test_id("activity-feed")).to_be_visible()
            for old_tab, new_address, section in (
                ("jobs", r"/system\?tab=history&show=jobs", "processing-jobs-inspection-section"),
                ("libraries", r"/settings\?tab=libraries", "processing-libraries-section"),
                ("audio-subtitles", r"/settings\?tab=rules", "processing-rule-set-workspace"),
                ("schedules", r"/settings\?tab=schedule", "processing-schedules-section"),
                ("maintenance", r"/settings\?tab=housekeeping", "processing-maintenance-section"),
            ):
                page.goto(f"{base}/processing?tab={old_tab}", wait_until="domcontentloaded")
                expect(page).to_have_url(re.compile(rf".*{new_address}$"))
                expect(page.get_by_test_id(section)).to_be_visible()
        finally:
            browser.close()
