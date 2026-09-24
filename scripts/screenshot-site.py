"""Screenshot every screen of Weir's web app, so a redesign can be reviewed by eye.

Brings up the .NET server against a fresh, disposable ``WEIR_HOME`` (twice: once left empty, once
seeded with representative data by plain SQL — see ``tests/e2e/weir/utils.py`` for the same pattern),
signs in without ever typing a password into the login form, and walks every screen the app has, in
both themes and at a desktop and a narrow width. Writes PNGs plus a ``contact-sheet.html`` to the
directory given on the command line, and prints a manifest.

This does not build anything. Before running it:

    (cd apps/web && npm ci && npm run build)
    dotnet build apps/server/src/Weir.Host

Usage (from the repository root)::

    python scripts/screenshot-site.py <output-dir>

Needs the Playwright browsers installed once: ``python -m playwright install chromium``.

The screen catalogue, server lifecycle, browser/capture helpers, sign-in and database seeding are
split out into the ``screenshot_site`` package next to this script (#747), by area rather than in
one file. This script stays the only supported entry point.
"""

from __future__ import annotations

import argparse
import importlib.util
import secrets
import shutil
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(REPO_ROOT))


def _load_contact_sheet_builder():
    """Import scripts/build-contact-sheet.py, whose kebab-case filename is not a legal module name."""

    path = Path(__file__).resolve().parent / "build-contact-sheet.py"
    spec = importlib.util.spec_from_file_location("weir_build_contact_sheet", path)
    if spec is None or spec.loader is None:  # pragma: no cover - defensive
        raise SystemExit(f"Could not load the contact sheet builder from {path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module.build_contact_sheet


build_contact_sheet = _load_contact_sheet_builder()

from playwright.sync_api import Browser, sync_playwright  # noqa: E402
from tests.e2e.weir import _runtime as runtime  # noqa: E402

from screenshot_site.auth import api_bootstrap, api_login  # noqa: E402
from screenshot_site.capture import (  # noqa: E402
    CRASH_TEXT,
    DESKTOP_VIEWPORT,
    THEME_STORAGE_KEY,
    THEMES,
    TIMEOUT_MS,
    WIDTHS,
    Shooter,
    new_context,
)
from screenshot_site.screens import ALL_SCREENS, GATED_SCREENS, NORMAL_SCREENS  # noqa: E402
from screenshot_site.seed import seed_representative_data  # noqa: E402
from screenshot_site.server_lifecycle import (  # noqa: E402
    check_prerequisites,
    fail,
    set_wizard_state,
    start_weir_server,
)

# THEME_STORAGE_KEY, CRASH_TEXT, fail, and everything above are re-exported (not just used locally)
# because scripts/capture-readme-screenshots.py loads this file as a module and reaches them as
# attributes of it (``harness.fail``, etc.) — it reuses this whole harness rather than starting a
# second server. Removing a name here without checking that script first will break it silently.
_ = (CRASH_TEXT, THEME_STORAGE_KEY, fail)


def run_scenario(browser: Browser, *, scenario: str, output_dir: Path) -> list[str]:
    print(f"\n=== scenario: {scenario} ===")
    session_secret = secrets.token_hex(32)
    server, home, base_url = start_weir_server(session_secret)
    shooter = Shooter(output_dir=output_dir, scenario=scenario)
    username = f"site-shots-{scenario}"
    password = secrets.token_urlsafe(24)  # generated, never typed into the login form
    try:
        # 1. Setup screen (first-run bootstrap form), before any admin exists — every combo.
        setup_screen = GATED_SCREENS[0]
        for theme in THEMES:
            for width_name, viewport in WIDTHS:
                ctx = new_context(browser, theme=theme, viewport=viewport)
                page = ctx.new_page()
                page.goto(base_url + setup_screen.path, wait_until="domcontentloaded")
                page.wait_for_selector(setup_screen.ready_selector, timeout=TIMEOUT_MS, state="visible")
                shooter.shoot(page, setup_screen.index, setup_screen.slug, theme, width_name, setup_screen.label)
                ctx.close()

        # 2. Bootstrap a throwaway admin over the API (never typed into the form).
        boot_ctx = new_context(browser, theme="dark", viewport=DESKTOP_VIEWPORT)
        boot_page = boot_ctx.new_page()
        boot_page.goto(base_url + "/", wait_until="domcontentloaded")
        api_bootstrap(boot_page, username, password)
        boot_ctx.close()

        # 3. Login screen, now that bootstrapping is no longer offered — every combo, unauthenticated.
        login_screen = GATED_SCREENS[1]
        for theme in THEMES:
            for width_name, viewport in WIDTHS:
                ctx = new_context(browser, theme=theme, viewport=viewport)
                page = ctx.new_page()
                page.goto(base_url + login_screen.path, wait_until="domcontentloaded")
                page.wait_for_selector(login_screen.ready_selector, timeout=TIMEOUT_MS, state="visible")
                shooter.shoot(page, login_screen.index, login_screen.slug, theme, width_name, login_screen.label)
                ctx.close()

        # 4. Sign in once (csrf, then login, same-origin fetch with credentials included) and keep
        # the resulting session cookie to replay into the other theme/width contexts below — that is
        # cookie reuse of one real session, not a second sign-in.
        auth_ctx = new_context(browser, theme="dark", viewport=DESKTOP_VIEWPORT)
        auth_page = auth_ctx.new_page()
        auth_page.goto(base_url + "/", wait_until="domcontentloaded")
        api_login(auth_page, username, password)
        storage_state = auth_ctx.storage_state()
        auth_ctx.close()

        # 5. Setup wizard: a fresh bootstrap always starts here. Capture every combo while it is
        # still pending, then move it out of the way for the rest of this scenario's screens.
        wizard_screen = GATED_SCREENS[2]
        for theme in THEMES:
            for width_name, viewport in WIDTHS:
                ctx = new_context(browser, theme=theme, viewport=viewport, storage_state=storage_state)
                page = ctx.new_page()
                page.goto(base_url + wizard_screen.path, wait_until="domcontentloaded")
                page.wait_for_selector(wizard_screen.ready_selector, timeout=TIMEOUT_MS, state="visible")
                shooter.shoot(page, wizard_screen.index, wizard_screen.slug, theme, width_name, wizard_screen.label)
                ctx.close()
        set_wizard_state(home, "skipped")

        if scenario == "seeded":
            seed_representative_data(home)

        # 6. Every remaining screen, addressed directly by its own URL and query params.
        for theme in THEMES:
            for width_name, viewport in WIDTHS:
                ctx = new_context(browser, theme=theme, viewport=viewport, storage_state=storage_state)
                page = ctx.new_page()
                for screen in NORMAL_SCREENS:
                    shooter.goto_and_shoot(page, base_url, screen, theme, width_name)
                ctx.close()
    finally:
        runtime.stop_server(server)
        shutil.rmtree(home, ignore_errors=True)
    return shooter.manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output_dir", type=Path, help="Directory to write PNGs and contact-sheet.html into")
    args = parser.parse_args()

    check_prerequisites()

    output_dir: Path = args.output_dir
    output_dir.mkdir(parents=True, exist_ok=True)

    stopped = runtime.reap_leftovers()
    if stopped:
        print(f"Stopped servers left behind by an earlier run: {', '.join(stopped)}")

    manifest: list[str] = []
    with sync_playwright() as p:
        browser = p.chromium.launch(args=["--force-color-profile=srgb"])
        try:
            for scenario in ("empty", "seeded"):
                manifest.extend(run_scenario(browser, scenario=scenario, output_dir=output_dir))
        finally:
            browser.close()

    build_contact_sheet(output_dir, ALL_SCREENS)

    manifest.sort()
    manifest_path = output_dir / "manifest.txt"
    manifest_path.write_text("\n".join(manifest) + "\n", encoding="utf-8")
    print(f"\n{len(manifest)} screenshots written to {output_dir}")
    print(f"Manifest: {manifest_path}")
    print(f"Contact sheet: {output_dir / 'contact-sheet.html'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
