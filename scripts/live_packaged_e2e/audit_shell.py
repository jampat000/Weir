"""``AuditShellMixin``: sidebar navigation, the desktop/mobile shell chrome, and the Processing,
Library and Logs (Activity) screens. Assumes ``AuditCore`` in the same instance.
"""

from __future__ import annotations

import re

from .config import BASE_URL


class AuditShellMixin:
    def open_sidebar(self, label: str) -> None:
        # A link that carries a count is named for it ("Processing, 2 working", "Activity, 1 need you").
        link = self.page.get_by_role("link", name=re.compile(rf"^{re.escape(label)}(?:,|$)"))
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
        # The mark, not its block: the name beside it folds away on collapse by design.
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
        """A tab of a setup area (Workflows, Connections, Rules, Performance) or of System.

        Each area is an entry of its own in the side menu, which marks the one showing, and has its tabs across
        the top of the page.
        """

        self.open_sidebar(sidebar)
        self.visible(
            self.page.get_by_role("link", name=sidebar, exact=True).and_(self.page.locator("[aria-current='page']")),
            f"{sidebar} marked as the current page in the side menu",
        )
        self.click(
            self.page.get_by_role("tab", name=tab, exact=True),
            f"open {sidebar} › {tab}",
        )
        self.visible(
            self.page.get_by_role("tab", name=tab, exact=True, selected=True),
            f"{sidebar} › {tab} selected",
        )

    def open_logs(self, source: str | None = None) -> None:
        """System › Logs, the one log of Weir's events, jobs and server log; ``source`` names a Source chip to press."""

        self.open_tab("System", "Logs")
        self.visible(self.page.get_by_test_id("log-summary"), "Logs summary")
        if source:
            chip = self.page.get_by_role("group", name="Source").get_by_role(
                "button", name=re.compile(rf"^{re.escape(source)}")
            )
            self.click(chip, f"press the {source} source chip")
            self.require(
                chip.get_attribute("aria-pressed") == "true",
                f"the {source} source chip is not pressed",
            )

    def tab_labels(self, tabs_test_id: str) -> list[str]:
        tabs = self.page.get_by_test_id(tabs_test_id).get_by_role("tab")
        return [label.strip() for label in tabs.all_text_contents()]

    def processing_live(self) -> None:
        self.open_sidebar("Dashboard")
        self.visible(self.page.get_by_test_id("processing-page"), "Dashboard page")
        self.visible(
            self.page.get_by_role("heading", name="Dashboard", exact=True),
            "Dashboard heading",
        )
        # The landing page is the Dashboard, every file's story is Activity, and the
        # events are System › Logs. Each setup area is an entry of its own.
        primary = self.page.get_by_role("navigation", name="Primary")
        labels = [text.strip() for text in primary.locator(".mm-sidebar-link-label").all_text_contents()]
        self.require(
            labels
            == [
                "Dashboard",
                "Activity",
                "Library",
                "Workflows",
                "Rules",
                "Media managers",
                "Performance",
                "Schedule",
                "Cleanup",
                "Alerts",
                "System",
            ],
            f"primary navigation is {labels}",
        )
        for retired in ("Home", "Processing", "Activity"):
            self.require(
                not self.page.get_by_role("link", name=retired, exact=True).count(),
                f"{retired} must not appear in the sidebar",
            )
        self.assert_no_visible_crash()
        self.screenshot("processing")
        self.record("Dashboard main screen and the side menu")

    def library(self) -> None:
        self.open_sidebar("Library")
        self.visible(self.page.get_by_test_id("library-page"), "Library page")
        self.assert_no_visible_crash()
        self.screenshot("library")
        self.record("Library screen")

    def history_activity(self) -> None:
        self.open_logs()
        self.visible(self.page.get_by_test_id("log-feed"), "Log")
        filters = self.visible(self.page.get_by_test_id("logs-controls"), "Log filters")
        self.require(
            self.page.get_by_text("All modules", exact=True).count() == 0,
            "Logs still shows a Module filter",
        )
        self.require(
            self.page.get_by_role("button", name="Apply filters", exact=True).count() == 0,
            "Logs still has an Apply button",
        )
        level = self.page.get_by_test_id("logs-level-picker")
        self.click(level, "open the level picker")
        # The picker offers only the levels the log has entries for, so this takes the first of those.
        first_level = self.visible(self.page.get_by_role("option").first, "a level the log has entries for")
        chosen_level = first_level.inner_text().split(" · ")[0].strip()
        self.click(first_level, "narrow the log to a level")
        self.click(level, "close the level picker")
        self.require(
            level.inner_text().strip() == chosen_level,
            f"the level picker does not say {chosen_level}",
        )
        filters.get_by_role("searchbox", name="Search the log").fill("audit")
        self.click(filters.get_by_test_id("logs-when-picker"), "open the time picker")
        self.click(
            self.page.get_by_role("option", name="Custom range", exact=True),
            "choose a custom range",
        )
        range_fields = self.visible(self.page.get_by_test_id("logs-range"), "custom range")
        range_fields.locator('input[type="datetime-local"]').nth(0).fill("2026-01-01T00:00")
        range_fields.locator('input[type="datetime-local"]').nth(1).fill("2026-12-31T23:59")
        self.click(
            self.page.get_by_role("button", name="Clear filters", exact=True),
            "clear the log's filters",
        )
        self.page.wait_for_timeout(500)
        self.require(
            level.inner_text().strip() == "All levels",
            "the log's filters did not clear",
        )
        self.visible(self.page.get_by_test_id("logs-export"), "Log export menu")
        self.screenshot("history-activity")
        self.record("Logs: one log of events, jobs and the server, its filters and its actions")
