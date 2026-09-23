from __future__ import annotations

import os
import re
from pathlib import Path

import pytest
from playwright.sync_api import expect, sync_playwright

from ._helpers import ensure_signed_in, open_sidebar, open_tab

pytestmark = [
    pytest.mark.weir_e2e,
    pytest.mark.skipif(
        os.environ.get("WEIR_E2E") != "1",
        reason="Weir E2E requires WEIR_E2E=1 (see tests/e2e/weir/conftest.py).",
    ),
]


def test_saved_state_persists_across_settings_and_processing(
    weir_shell: str,
    weir_home: str,
) -> None:
    base = weir_shell.rstrip("/")
    tv_watch = Path(weir_home) / "e2e" / "tv-watch-missing"
    tv_output = Path(weir_home) / "e2e" / "tv-output"
    tv_output.mkdir(parents=True, exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        try:
            page = browser.new_page()
            page.set_default_timeout(30_000)

            ensure_signed_in(page, base)

            # The setup wizard reopens from System › About, where it folds away because it
            # is run once. There is no display density setting, in the wizard or on the page.
            open_sidebar(page, "System")
            expect(page.get_by_test_id("suite-settings-global")).to_be_visible()
            page.get_by_role("heading", name="Setup wizard", exact=True).click()
            page.get_by_test_id("suite-settings-open-setup-wizard").click()
            expect(page).to_have_url(re.compile(r".*/setup-wizard"))
            expect(page.get_by_role("heading", name="Set up Weir")).to_be_visible()
            expect(page.get_by_text("Display density", exact=False)).to_have_count(0)
            page.get_by_test_id("setup-wizard-skip").click()
            expect(page).to_have_url(re.compile(r".*/(?:$|[?#])"))
            expect(page.get_by_test_id("processing-page")).to_be_visible()
            expect(page.locator("html")).not_to_have_attribute("data-mm-density", re.compile(".*"))

            # Libraries is where Settings opens.
            open_tab(page, "Settings", "Libraries")
            libraries = page.get_by_test_id("processing-libraries-section")
            expect(libraries).to_be_visible()
            libraries.get_by_role("button", name="Edit", exact=True).nth(1).click()
            form = page.get_by_test_id("processing-library-form")
            form.get_by_role("textbox", name="Watched folder").fill(str(tv_watch))
            form.get_by_role("textbox", name="Output folder").fill(str(tv_output))
            page.get_by_test_id("processing-library-save").click()
            expect(form).to_have_count(0)
            open_sidebar(page, "Processing")
            expect(page.get_by_test_id("processing-page")).to_be_visible()
            open_tab(page, "Settings", "Libraries")
            expect(page.get_by_test_id("processing-libraries-section")).to_contain_text(str(tv_watch))
        finally:
            browser.close()
