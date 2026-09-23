from __future__ import annotations

from playwright.sync_api import Page, expect

BOOTSTRAP_USER = "e2e-shell-admin"
BOOTSTRAP_PASS = "e2e-shell-pass-min8"
URL_ASSERT_MS = 20_000
# Creating the admin and signing in both hash a password (Argon2id), and skipping the setup wizard saves
# several settings. Each submit waits for its own form to go away, so a pending request is never clicked
# again and a failed one fails here instead of in the next step.
SUBMIT_SETTLE_MS = 15_000
# First-run setup, sign-in, the setup wizard, then the shell: each screen at most once, plus spare turns
# for a screen that was replaced between being seen and being checked.
_MAX_SCREENS = 6


def ensure_signed_in(page: Page, base_url: str) -> None:
    """Open Weir and get to the signed-in shell, creating the admin and skipping the setup wizard if asked."""

    page.goto(f"{base_url.rstrip('/')}/", wait_until="domcontentloaded")
    setup = page.get_by_test_id("setup-username")
    login = page.get_by_test_id("login-username")
    wizard_skip = page.get_by_test_id("setup-wizard-skip")
    shell = page.get_by_test_id("shell-ready")
    for _ in range(_MAX_SCREENS):
        expect(setup.or_(login).or_(wizard_skip).or_(shell).first).to_be_visible(timeout=URL_ASSERT_MS)
        if shell.is_visible():
            return
        if setup.is_visible():
            setup.fill(BOOTSTRAP_USER)
            page.get_by_test_id("setup-password").fill(BOOTSTRAP_PASS)
            page.get_by_test_id("setup-submit").click()
            expect(setup).to_be_hidden(timeout=SUBMIT_SETTLE_MS)
        elif login.is_visible():
            login.fill(BOOTSTRAP_USER)
            page.get_by_test_id("login-password").fill(BOOTSTRAP_PASS)
            page.get_by_test_id("login-submit").click()
            expect(login).to_be_hidden(timeout=SUBMIT_SETTLE_MS)
        elif wizard_skip.is_visible():
            expect(page.get_by_role("heading", name="Set up Weir")).to_be_visible()
            wizard_skip.click()
            expect(wizard_skip).to_be_hidden(timeout=SUBMIT_SETTLE_MS)

    expect(shell).to_be_visible(timeout=URL_ASSERT_MS)


def open_sidebar(page: Page, label: str) -> None:
    page.get_by_role("link", name=label, exact=True).click()


def open_tab(page: Page, sidebar: str, tab: str) -> None:
    """A tab on Settings or System: the side menu entry, then the tab across the top."""

    open_sidebar(page, sidebar)
    selected = page.get_by_role("tab", name=tab, exact=True)
    selected.click()
    expect(selected).to_have_attribute("aria-selected", "true")


def open_logs(page: Page, show: str = "Events") -> None:
    """System › Logs: Weir's own events, its jobs and the server log. Each file's story is in History.

    ``show`` is the label of one option in its Show choice.
    """

    open_tab(page, "System", "Logs")
    choice = page.get_by_test_id("settings-history-show")
    if show != "Events":
        choice.select_option(label=show)
    expect(choice.locator("option:checked")).to_have_text(show)
