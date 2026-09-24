"""Visual regression smoke tests for high-risk pages.

Run with WEIR_E2E=1. Screenshots are saved to artifacts/screenshots/ for
visual inspection. The artifacts/ directory is .gitignored so no pixel-exact
baselines are committed; these are informational smoke checks. What is asserted is
the structure of each screen: Processing at "/", Settings and System as two rows of
tabs, and System › Logs with its history statement.

Usage:
    WEIR_E2E=1 pytest tests/e2e/weir/test_visual_smoke_audit.py -v

To capture fresh screenshots (e.g. after intentional UI changes):
    WEIR_E2E=1 pytest tests/e2e/weir/test_visual_smoke_audit.py -v
    (screenshots are always overwritten on each run)
"""

from __future__ import annotations

import os
import re
from pathlib import Path

import pytest
from playwright.sync_api import expect, sync_playwright

from ._helpers import ensure_signed_in, open_logs, open_sidebar, open_tab

pytestmark = [
    pytest.mark.weir_e2e,
    pytest.mark.skipif(
        os.environ.get("WEIR_E2E") != "1",
        reason="Weir E2E requires WEIR_E2E=1 (see tests/e2e/weir/conftest.py).",
    ),
]

# Directory where screenshots are persisted (informational, .gitignored).
_SCREENSHOT_DIR = Path(__file__).resolve().parents[3] / "artifacts" / "screenshots"


def _save_screenshot(page, test_name: str) -> None:
    """Save a viewport-only screenshot to artifacts/screenshots/<test_name>.png."""
    _SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)
    path = _SCREENSHOT_DIR / f"{test_name}.png"
    page.screenshot(path=str(path), full_page=False)


def _scroll_to_top(page) -> None:
    """Reset the document immediately, even when smooth scrolling is enabled."""
    page.evaluate(
        """() => {
            const root = document.documentElement;
            const previous = root.style.scrollBehavior;
            root.style.scrollBehavior = 'auto';
            window.scrollTo(0, 0);
            root.style.scrollBehavior = previous;
        }"""
    )
    page.wait_for_function("window.scrollY === 0")


def _assert_no_error_state(page) -> None:
    """Assert no error-boundary overlay or generic crash message is visible."""
    expect(page.get_by_test_id("error-boundary")).not_to_be_visible(timeout=2_000)
    expect(page.get_by_text("Something went wrong", exact=False)).not_to_be_visible(
        timeout=2_000
    )


def _assert_document_owns_vertical_scroll(page) -> None:
    """Signed-in pages must not trap wheel input inside the main shell."""
    scroll = page.evaluate(
        """() => {
            const main = document.querySelector('.mm-main');
            const layout = document.querySelector('.mm-app-layout');
            return {
                mainOverflowY: getComputedStyle(main).overflowY,
                layoutOverflowY: getComputedStyle(layout).overflowY,
                scrollingElement: document.scrollingElement?.tagName,
            };
        }"""
    )
    assert scroll["mainOverflowY"] not in {"auto", "scroll"}, scroll
    assert scroll["layoutOverflowY"] not in {"auto", "scroll"}, scroll
    assert scroll["scrollingElement"] == "HTML", scroll


def _assert_tab_workspace(page, *, page_test_id: str, tabs_test_id: str) -> None:
    """A page uses the shared themed tab bar and accessible panel contract."""
    workspace = page.get_by_test_id(page_test_id)
    expect(workspace).to_have_class(re.compile(r"\bmm-workspace-page\b"))
    tabs = page.get_by_test_id(tabs_test_id)
    expect(tabs).to_have_class(re.compile(r"\bmm-workspace-tabs\b"))
    active_tab = tabs.locator("[role='tab'][aria-selected='true']")
    expect(active_tab).to_have_count(1)
    panel_id = active_tab.get_attribute("aria-controls")
    assert panel_id, "active workspace tab must identify its panel"
    panel = page.locator(f"#{panel_id}")
    expect(panel).to_be_visible()
    expect(panel).to_have_attribute("aria-labelledby", active_tab.get_attribute("id"))


def test_old_dashboard_address_is_not_found(weir_shell: str) -> None:
    """A retired address such as /dashboard gets the not-found page, which offers the way to Processing.

    Only addresses a user could still have saved are redirected; /dashboard is not one of them.
    """
    base = weir_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page(viewport={"width": 1600, "height": 900})
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)

            page.goto(f"{base}/dashboard", wait_until="domcontentloaded")
            expect(page).to_have_url(re.compile(r".*/dashboard"))
            expect(
                page.get_by_role(
                    "heading", name="This page doesn't exist.", exact=True
                )
            ).to_be_visible()
            expect(page.get_by_role("link", name="Dashboard", exact=True)).to_have_count(0)
            _assert_no_error_state(page)

            page.get_by_role("link", name="Go to Processing", exact=True).click()
            expect(page).to_have_url(re.compile(r".*/(?:$|[?#])"))
            expect(page.get_by_role("heading", name="Processing", exact=True)).to_be_visible()
        finally:
            browser.close()


def test_processing_is_the_landing_page(weir_shell: str) -> None:
    """Weir lands on Processing at "/": every file Weir is working on. There is no Home page."""
    base = weir_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page(viewport={"width": 1600, "height": 900})
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)
            page.goto(f"{base}/", wait_until="domcontentloaded")

            expect(page.get_by_test_id("processing-page")).to_be_visible()
            expect(page.get_by_role("heading", name="Processing", exact=True)).to_be_visible()
            expect(page.get_by_role("heading", name="Home", exact=True)).to_have_count(0)
            # Page content only: the shell brand line is replaced by the Weir rename (#458).
            expect(page.locator("main").get_by_text("your library", exact=False)).to_have_count(0)
            _assert_document_owns_vertical_scroll(page)
            _assert_no_error_state(page)
            _save_screenshot(page, "processing")
        finally:
            browser.close()


def test_history_says_how_far_back_it_goes(weir_shell: str) -> None:
    """System › Logs states its history horizon plainly (#469)."""
    base = weir_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page(viewport={"width": 1600, "height": 900})
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)
            open_logs(page)

            expect(page).to_have_url(re.compile(r".*/system\?tab=logs(?:$|[&#])"))
            expect(page.get_by_test_id("activity-retention")).to_contain_text("History goes back 90 days")
            expect(page.get_by_test_id("activity-feed")).to_be_visible()
            _assert_no_error_state(page)
            _save_screenshot(page, "history")
        finally:
            browser.close()


def test_settings_and_system_tabs_render(weir_shell: str) -> None:
    """Every Settings and System tab opens, and the document keeps the scroll."""
    base = weir_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page(viewport={"width": 1600, "height": 900})
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)

            for label, page_test_id, tabs_test_id in (
                ("Settings", "suite-settings-page", "settings-section-tabs"),
                ("System", "suite-system-page", "system-section-tabs"),
            ):
                open_sidebar(page, label)
                expect(page.get_by_test_id(page_test_id)).to_be_visible()
                tabs = page.get_by_test_id(tabs_test_id).get_by_role("tab")
                for index in range(tabs.count()):
                    tab = tabs.nth(index)
                    tab.click()
                    expect(tab).to_have_attribute("aria-selected", "true")
                    _assert_no_error_state(page)

            open_tab(page, "System", "Security")
            expect(page.get_by_test_id("suite-settings-security")).to_be_visible()
            _scroll_to_top(page)
            page.screenshot(
                path=str(_SCREENSHOT_DIR / "system-security-full.png"),
                full_page=True,
            )

            open_tab(page, "System", "About")
            expect(page.get_by_test_id("suite-settings-global")).to_be_visible()

            _assert_document_owns_vertical_scroll(page)
            _assert_no_error_state(page)
            _scroll_to_top(page)
            _save_screenshot(page, "system-instance")
            page.screenshot(
                path=str(_SCREENSHOT_DIR / "system-instance-full.png"),
                full_page=True,
            )
        finally:
            browser.close()


def test_settings_and_system_share_themed_tabs_and_responsive_layout(
    weir_shell: str,
) -> None:
    """Settings and System use the shared horizontal tab bar without page overflow."""
    base = weir_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            context = browser.new_context(viewport={"width": 1600, "height": 900})
            page = context.new_page()
            page.set_default_timeout(30_000)
            ensure_signed_in(page, base)

            for label, page_test_id, tabs_test_id, screenshot_name in (
                ("Settings", "suite-settings-page", "settings-section-tabs", "settings-workspace"),
                ("System", "suite-system-page", "system-section-tabs", "system-workspace"),
            ):
                open_sidebar(page, label)
                _assert_tab_workspace(
                    page,
                    page_test_id=page_test_id,
                    tabs_test_id=tabs_test_id,
                )
                _assert_document_owns_vertical_scroll(page)
                _assert_no_error_state(page)
                _scroll_to_top(page)
                _save_screenshot(page, screenshot_name)

                mobile_page = context.new_page()
                try:
                    mobile_page.set_viewport_size({"width": 390, "height": 844})
                    mobile_page.set_default_timeout(30_000)
                    mobile_page.goto(page.url, wait_until="domcontentloaded")
                    _assert_tab_workspace(
                        mobile_page,
                        page_test_id=page_test_id,
                        tabs_test_id=tabs_test_id,
                    )
                    assert mobile_page.evaluate(
                        "document.documentElement.scrollWidth <= document.documentElement.clientWidth"
                    ), f"{label} overflows the narrow viewport"
                    _assert_document_owns_vertical_scroll(mobile_page)
                    _assert_no_error_state(mobile_page)
                    _save_screenshot(mobile_page, f"{screenshot_name}-mobile")
                finally:
                    mobile_page.close()
        finally:
            browser.close()


def test_rules_editor_renders(weir_shell: str) -> None:
    """Settings › Rules: the full audio & subtitle profile editor stays readable at desktop width."""
    base = weir_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page(viewport={"width": 1600, "height": 900})
            page.set_default_timeout(30_000)
            ensure_signed_in(page, base)

            open_tab(page, "Settings", "Rules")
            expect(page.get_by_test_id("processing-rule-set-workspace")).to_be_visible()
            page.get_by_role("button", name="New profile →", exact=True).click()
            # The profile bar: the picker, the name and who uses it on one line; the field is "Name".
            expect(page.get_by_test_id("rule-set-profile-bar").get_by_label("Name", exact=True)).to_be_visible()
            expect(page.get_by_text("Audio order", exact=True)).not_to_be_visible()

            _assert_document_owns_vertical_scroll(page)
            _assert_no_error_state(page)
            _scroll_to_top(page)
            _save_screenshot(page, "settings-rules")
            page.screenshot(
                path=str(_SCREENSHOT_DIR / "settings-rules-full.png"),
                full_page=True,
            )
            page.set_viewport_size({"width": 390, "height": 844})
            # At phone width the side menu slides out; it turns hidden only once the slide has finished.
            expect(page.get_by_test_id("shell-nav-toggle")).to_be_visible()
            expect(page.locator("#mm-primary-sidebar")).to_be_hidden()
            _scroll_to_top(page)
            _save_screenshot(page, "settings-rules-mobile")
        finally:
            browser.close()
