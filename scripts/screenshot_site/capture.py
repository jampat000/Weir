"""Driving the browser and taking the actual screenshots: viewports, theme, and the ``Shooter``
that names and writes each PNG. Split out of the main script (#747).

Theme is driven the way the app itself drives it: ``apps/web/src/main.tsx`` applies
``data-mm-theme`` from this same localStorage key before React ever renders (so there is no flash
of the wrong theme) — the identical mechanism the top-bar toggle uses (``persistAppTheme`` in
``apps/web/src/lib/ui/app-theme.ts``). Setting the key an app-shell page already writes, before
navigation, is that mechanism — not a CSS override.
"""

from __future__ import annotations

import contextlib
from dataclasses import dataclass, field
from pathlib import Path

from playwright.sync_api import Browser, BrowserContext, Page

from .screens import Screen
from .server_lifecycle import fail

DESKTOP_VIEWPORT = {"width": 1440, "height": 1000}
NARROW_VIEWPORT = {"width": 390, "height": 844}
WIDTHS: list[tuple[str, dict[str, int]]] = [
    ("desktop", DESKTOP_VIEWPORT),
    ("narrow", NARROW_VIEWPORT),
]
THEMES = ["dark", "light"]
THEME_STORAGE_KEY = "weir-app-theme"  # apps/web/src/lib/ui/app-theme.ts

CRASH_TEXT = "Something went wrong"
TIMEOUT_MS = 30_000


def new_context(
    browser: Browser, *, theme: str, viewport: dict[str, int], storage_state: dict | None = None
) -> BrowserContext:
    ctx = browser.new_context(
        viewport=viewport,
        device_scale_factor=2,
        storage_state=storage_state,
        color_scheme=theme,
    )
    ctx.add_init_script(f"try {{ localStorage.setItem({THEME_STORAGE_KEY!r}, {theme!r}); }} catch (e) {{}}")
    ctx.set_default_timeout(TIMEOUT_MS)
    return ctx


@dataclass
class Shooter:
    output_dir: Path
    scenario: str
    manifest: list[str] = field(default_factory=list)

    def shoot(self, page: Page, screen_idx: int, slug: str, theme: str, width_name: str, label: str) -> None:
        crash = page.get_by_text(CRASH_TEXT, exact=False)
        if crash.count():
            fail(
                f"{label} ({self.scenario}/{theme}/{width_name}) shows an error boundary: {crash.first.inner_text()!r}"
            )
        name = f"{screen_idx:02d}-{slug}--{self.scenario}--{theme}--{width_name}.png"
        target = self.output_dir / name
        page.screenshot(path=str(target), full_page=True)
        self.manifest.append(f"{name}\t{label} ({self.scenario}, {theme}, {width_name})")
        print(f"  {name}")

    def goto_and_shoot(self, page: Page, base_url: str, screen: Screen, theme: str, width_name: str) -> None:
        page.goto(base_url + screen.path, wait_until="domcontentloaded")
        page.wait_for_selector(screen.ready_selector, timeout=TIMEOUT_MS, state="visible")
        # Activity's live feed and Processing's polling keep the network busy by design.
        with contextlib.suppress(Exception):
            page.wait_for_load_state("networkidle", timeout=3_000)
        page.wait_for_timeout(200)
        self.shoot(page, screen.index, screen.slug, theme, width_name, screen.label)
