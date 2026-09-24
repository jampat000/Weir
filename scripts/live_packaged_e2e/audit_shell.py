"""``AuditShellMixin``: sidebar navigation, the desktop/mobile shell chrome, and the Processing,
Library and Logs (Activity) screens. Assumes ``AuditCore`` in the same instance.
"""

from __future__ import annotations

from .config import BASE_URL


class AuditShellMixin:
    def open_sidebar(self, label: str) -> None:
        link = self.page.get_by_role("link", name=label, exact=True)
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
        # The mark, not its box: the wordmark beside it folds away on collapse by design.
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
        """A tab on Settings or System (3.2): the side menu entry, then the tab across the top."""

        self.open_sidebar(sidebar)
        self.click(
            self.page.get_by_role("tab", name=tab, exact=True),
            f"open {sidebar} › {tab}",
        )
        self.visible(
            self.page.get_by_role("tab", name=tab, exact=True, selected=True),
            f"{sidebar} › {tab} selected",
        )

    def open_logs(self, show: str = "Events") -> None:
        """System › Logs, Weir's own events; ``show`` is the label of one option in its Show choice."""

        self.open_tab("System", "Logs")
        choice = self.visible(
            self.page.get_by_test_id("settings-history-show"), "Logs Show choice"
        )
        if show != "Events":
            choice.select_option(label=show)
        self.require(
            (choice.locator("option:checked").text_content() or "").strip() == show,
            f"Logs is not showing {show}",
        )

    def tab_labels(self, tabs_test_id: str) -> list[str]:
        tabs = self.page.get_by_test_id(tabs_test_id).get_by_role("tab")
        return [label.strip() for label in tabs.all_text_contents()]

    def processing_live(self) -> None:
        self.open_sidebar("Processing")
        self.visible(self.page.get_by_test_id("processing-page"), "Processing page")
        self.visible(
            self.page.get_by_role("heading", name="Processing", exact=True),
            "Processing heading",
        )
        # Five places since 3.2. Home became Processing, the dashboard folded into it (#459), every
        # file's story is History, and the 3.1 Activity page is System › Logs.
        primary = self.page.get_by_role("navigation", name="Primary")
        labels = [text.strip() for text in primary.locator(".mm-sidebar-link-label").all_text_contents()]
        self.require(
            labels == ["Processing", "History", "Library", "Settings", "System"],
            f"primary navigation is {labels}",
        )
        for retired in ("Home", "Dashboard", "Activity"):
            self.require(
                not self.page.get_by_role("link", name=retired, exact=True).count(),
                f"{retired} must not appear in the sidebar",
            )
        self.assert_no_visible_crash()
        self.screenshot("processing")
        self.record("Processing main screen and the five-place side menu")

    def library(self) -> None:
        self.open_sidebar("Library")
        self.visible(self.page.get_by_test_id("library-page"), "Library page")
        self.assert_no_visible_crash()
        self.screenshot("library")
        self.record("Library screen")

    def history_activity(self) -> None:
        self.open_logs()
        self.visible(self.page.get_by_test_id("activity-feed"), "Activity feed")
        # Scoped to the filters: the Show choice above them is a select too.
        filters = self.visible(
            self.page.get_by_test_id("activity-filters"), "Activity filters"
        )
        selects = filters.locator("select")
        self.require(selects.count() >= 2, "Activity filters are incomplete")
        self.require(
            self.page.get_by_text("All modules", exact=True).count() == 0,
            "Activity still shows a Module filter",
        )
        if selects.nth(0).locator("option").count() > 1:
            selects.nth(0).select_option(index=1)
        filters.get_by_placeholder("Search titles and details").fill("audit")
        filters.locator('input[type="datetime-local"]').nth(0).fill("2026-01-01T00:00")
        filters.locator('input[type="datetime-local"]').nth(1).fill("2026-12-31T23:59")
        self.click(
            filters.get_by_role("button", name="Apply filters", exact=True),
            "apply Activity filters",
        )
        self.visible(
            self.page.get_by_test_id("activity-summary").get_by_text("matching your filters"),
            "Activity active filter state",
        )
        # Exactly "Clear →": its neighbour "Clear all history →" deletes Activity.
        self.click(
            filters.get_by_role("button", name="Clear →", exact=True),
            "clear Activity filters",
        )
        self.page.wait_for_timeout(500)
        self.require(
            not self.page.get_by_test_id("activity-summary").get_by_text("matching your filters").count(),
            "Activity filters did not clear",
        )
        self.screenshot("history-activity")
        self.record("Logs: Weir's own events, filters, and clear action")
