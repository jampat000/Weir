"""Visual regression smoke tests for high-risk pages.

Run with MEDIAMOP_E2E=1. Screenshots are saved to artifacts/screenshots/ for
visual inspection. The artifacts/ directory is .gitignored so no pixel-exact
baselines are committed; these are informational smoke checks.

Usage:
    MEDIAMOP_E2E=1 pytest tests/e2e/mediamop/test_visual_smoke_audit.py -v

To capture fresh screenshots (e.g. after intentional UI changes):
    MEDIAMOP_E2E=1 pytest tests/e2e/mediamop/test_visual_smoke_audit.py -v
    (screenshots are always overwritten on each run)
"""

from __future__ import annotations

import os
import re
from pathlib import Path

import pytest
from playwright.sync_api import expect, sync_playwright

from ._helpers import ensure_signed_in, open_sidebar

pytestmark = [
    pytest.mark.mediamop_e2e,
    pytest.mark.skipif(
        os.environ.get("MEDIAMOP_E2E") != "1",
        reason="MediaMop E2E requires MEDIAMOP_E2E=1 (see tests/e2e/mediamop/conftest.py).",
    ),
]

# Directory where screenshots are persisted (informational, .gitignored).
_SCREENSHOT_DIR = Path(__file__).resolve().parents[3] / "artifacts" / "screenshots"


def _save_screenshot(page, test_name: str) -> None:
    """Save a viewport-only screenshot to artifacts/screenshots/<test_name>.png."""
    _SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)
    path = _SCREENSHOT_DIR / f"{test_name}.png"
    page.screenshot(path=str(path), full_page=False)


def _assert_no_error_state(page) -> None:
    """Assert no error-boundary overlay or generic crash message is visible."""
    expect(page.get_by_test_id("error-boundary")).not_to_be_visible(timeout=2_000)
    expect(page.get_by_text("Something went wrong", exact=False)).not_to_be_visible(
        timeout=2_000
    )


def _assert_settings_workspace_edges_align(page) -> None:
    """The desktop navigation rail and active settings panel share both edges."""
    edges = page.get_by_test_id("settings-workspace").evaluate(
        """workspace => {
            const nav = workspace
                .querySelector('.mm-workspace-rail__navigation')
                .getBoundingClientRect();
            const panel = workspace
                .querySelector('#settings-panel')
                .getBoundingClientRect();
            return {
                top: Math.abs(nav.top - panel.top),
                bottom: Math.abs(nav.bottom - panel.bottom),
            };
        }"""
    )
    assert edges["top"] <= 1, f"settings workspace top edges differ: {edges}"
    assert edges["bottom"] <= 1, f"settings workspace bottom edges differ: {edges}"


def _assert_tab_workspace(page, *, page_test_id: str, tabs_test_id: str) -> None:
    """A module workspace uses the shared, accessible tab and panel contract."""
    workspace = page.get_by_test_id(page_test_id)
    expect(workspace).to_have_class(re.compile(r"\bmm-workspace-page--tabs\b"))
    tabs = page.get_by_test_id(tabs_test_id)
    expect(tabs).to_have_class(re.compile(r"\bmm-workspace-tabs\b"))
    active_tab = tabs.locator("[role='tab'][aria-selected='true']")
    expect(active_tab).to_have_count(1)
    panel_id = active_tab.get_attribute("aria-controls")
    assert panel_id, "active workspace tab must identify its panel"
    panel = page.locator(f"#{panel_id}")
    expect(panel).to_be_visible()
    expect(panel).to_have_attribute("aria-labelledby", active_tab.get_attribute("id"))


def test_dashboard_renders_without_error(mediamop_shell: str) -> None:
    """Dashboard page loads, shows key structural elements, and is error-free."""
    base = mediamop_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page()
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)

            open_sidebar(page, "Dashboard")
            expect(page).to_have_url(re.compile(r".*/(?:$|[/?#])"))

            expect(page.get_by_test_id("dashboard-page")).to_be_visible()
            expect(page.get_by_test_id("dashboard-status-strip")).to_be_visible()
            expect(page.get_by_test_id("dashboard-module-cards")).to_be_visible()

            _assert_no_error_state(page)
            _save_screenshot(page, "dashboard")
        finally:
            browser.close()


def test_settings_general_tab_renders(mediamop_shell: str) -> None:
    """Every Settings tab keeps its desktop navigation and panel edges aligned."""
    base = mediamop_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page(viewport={"width": 1600, "height": 900})
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)

            open_sidebar(page, "Settings")
            expect(page).to_have_url(re.compile(r".*/settings"))

            expect(page.get_by_test_id("suite-settings-page")).to_be_visible()
            expect(page.get_by_test_id("suite-settings-global")).to_be_visible()

            settings_tabs = page.locator(".mm-settings-nav__button")
            for index in range(settings_tabs.count()):
                settings_tab = settings_tabs.nth(index)
                settings_tab.click()
                expect(settings_tab).to_have_attribute("aria-selected", "true")
                _assert_settings_workspace_edges_align(page)

            page.get_by_role("tab", name="General", exact=True).click()
            expect(page.get_by_test_id("suite-settings-global")).to_be_visible()
            _assert_settings_workspace_edges_align(page)

            _assert_no_error_state(page)
            _save_screenshot(page, "settings-general")
        finally:
            browser.close()


def test_module_workspaces_share_tabs_and_responsive_layout(
    mediamop_shell: str,
) -> None:
    """Refiner and Pruner use one tab workspace without narrow-screen overflow."""
    base = mediamop_shell.rstrip("/")
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            context = browser.new_context(viewport={"width": 1600, "height": 900})
            page = context.new_page()
            page.set_default_timeout(30_000)
            ensure_signed_in(page, base)

            for label, page_test_id, tabs_test_id, screenshot_name in (
                (
                    "Refiner",
                    "refiner-scope-page",
                    "refiner-section-tabs",
                    "refiner-workspace",
                ),
                (
                    "Pruner",
                    "pruner-scope-page",
                    "pruner-top-level-tabs",
                    "pruner-workspace",
                ),
            ):
                open_sidebar(page, label)
                _assert_tab_workspace(
                    page,
                    page_test_id=page_test_id,
                    tabs_test_id=tabs_test_id,
                )
                _assert_no_error_state(page)
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
                        "document.documentElement.scrollWidth <= "
                        "document.documentElement.clientWidth"
                    ), f"{label} workspace overflows the narrow viewport"
                    _assert_no_error_state(mobile_page)
                    _save_screenshot(mobile_page, f"{screenshot_name}-mobile")
                finally:
                    mobile_page.close()
        finally:
            browser.close()
