"""``AuditSettingsMixin``: the setup areas, History and Logs jobs, and every System tab except
Alerts and Media managers (those are ``AuditNotificationsMixin``). Assumes ``AuditCore`` and
``AuditShellMixin`` (``open_sidebar``, ``open_tab``, ``open_logs``, ``tab_labels``) in the same
instance.
"""

from __future__ import annotations

from .config import TIMEOUT_MS


class AuditSettingsMixin:
    def settings_tabs(self) -> None:
        expected = {
            "Workflows": {
                "Workflows": "processing-libraries-section",
                "Schedule": "processing-schedules-section",
            },
            "Connections": {
                "Media managers": "suite-settings-media-managers",
                "Download clients": "suite-settings-download-clients-tab",
                "Alerts": "suite-settings-notifications",
            },
            "Rules": {
                "Profiles": "processing-rule-set-workspace",
                "Playback devices": "processing-direct-play-section",
            },
            "Performance": {
                "Speed": "processing-process-settings",
                "Cleanup": "processing-maintenance-section",
                "Weir's timers": "processing-timers-section",
            },
        }
        # Each setup area is an entry of its own in the side menu, with its tabs across the top of the page.
        for area, tabs in expected.items():
            self.open_sidebar(area)
            self.visible(self.page.get_by_test_id("suite-settings-page"), f"{area} page")
            self.visible(
                self.page.get_by_role("heading", level=1, name=area, exact=True),
                f"{area} page title",
            )
            self.require(
                self.tab_labels("setup-area-tabs") == list(tabs),
                f"{area} does not offer the tabs {list(tabs)}",
            )
            for tab, test_id in tabs.items():
                self.open_tab(area, tab)
                self.visible(self.page.get_by_test_id(test_id), f"{area} › {tab} panel")
                self.workflow_and_schedule_checks(tab)

        self.require(
            not self.page.get_by_test_id("settings-section-tabs").count(),
            "Settings still has a row of tabs of its own",
        )
        self.screenshot("settings")
        self.record("setup areas: every tab of Workflows, Connections, Rules and Performance")

    def workflow_and_schedule_checks(self, tab: str) -> None:
        if tab == "Workflows":
            self.visible(
                self.page.get_by_test_id("workflow-kind-badge").first,
                "each workflow says whether it is Weir only or linked to a media manager",
            )
            edit_buttons = self.page.get_by_role("button", name="Edit", exact=True)
            if edit_buttons.count():
                self.click(edit_buttons.first, "open workflow editor")
                self.visible(
                    self.page.get_by_test_id("processing-library-form"),
                    "workflow form",
                )
                cancel = self.page.get_by_role("button", name="Cancel", exact=True)
                if cancel.count():
                    self.click(cancel.last, "cancel workflow editor")
        elif tab == "Schedule":
            # A week per library, the time zone above them (canvas board 6).
            self.visible(
                self.page.get_by_text("Time zone", exact=True),
                "time zone control",
            )
            self.require(
                self.page.get_by_test_id("schedule-library-row").count() > 0,
                "no library weeks on Schedule",
            )

    def history_and_jobs(self) -> None:
        # History: every file Weir has touched, with the open file's record beside the list.
        self.open_sidebar("History")
        self.visible(self.page.get_by_test_id("history-page"), "History page")
        chips = self.page.get_by_role("group", name="Show").get_by_role("button")
        # Needs you is every file waiting on a person, as the sidebar's badge counts them, in whichever group it
        # is; a skip is its own neutral group rather than counting as Failed, on_hold sits under On hold, and
        # Kept lists the files the owner chose to keep without processing.
        expected_chip_labels = [
            "All",
            "In progress",
            "Finished",
            "Needs you",
            "On hold",
            "Skipped",
            "Failed",
            "Kept",
        ]
        chip_texts = chips.all_inner_texts()
        self.require(
            len(chip_texts) == len(expected_chip_labels)
            and all(
                text.startswith(label)
                for text, label in zip(chip_texts, expected_chip_labels)
            ),
            f"History shows {chip_texts!r}, not {expected_chip_labels!r} in order",
        )
        # A fresh install has no files, so assert whichever of the two states is real, and never
        # that the page rendered nothing at all.
        if self.page.get_by_test_id("history-detail").count():
            self.visible(self.page.get_by_test_id("history-detail"), "History open file")
        else:
            self.require(
                self.page.get_by_text("Nothing yet.", exact=False).count() > 0
                or self.page.get_by_text("No file matches", exact=False).count() > 0,
                "History showed neither files nor its empty state",
            )
        search = self.page.get_by_role("searchbox", name="Find a file")
        search.fill("audit")
        search.press("Enter")
        self.visible(self.page.get_by_test_id("history-page"), "History after a search")
        self.screenshot("history")

        self.open_logs("Weir's jobs")
        self.visible(
            self.page.get_by_test_id("processing-jobs-inspection-section"),
            "Logs jobs list",
        )
        self.screenshot("logs-jobs")
        self.record("History: every file and its record; Logs: Weir's jobs")

    def system_instance_and_setup(self) -> None:
        self.open_sidebar("System")
        self.visible(self.page.get_by_test_id("suite-system-page"), "System page")
        labels = self.tab_labels("system-section-tabs")
        self.require(
            labels == ["About", "Backups", "Security", "Logs"],
            f"System tabs are {labels}",
        )
        self.visible(
            self.page.get_by_test_id("suite-settings-global"), "System › About"
        )
        self.require(
            self.page.get_by_text("What Weir works with", exact=True).count()
            > 0,
            "runtime facts are missing from About",
        )
        # The packaged build must still link out to source and licence, not just the dev build.
        self.require(
            self.page.get_by_test_id("about-source-code-link").get_attribute("href")
            == "https://github.com/jampat000/Weir",
            "About is missing the Source code link",
        )
        self.require(
            self.page.get_by_text("AGPL-3.0-or-later", exact=False).count() > 0,
            "About is missing the licence name",
        )
        # Display density was removed in 3.2 and must not come back.
        self.require(
            not self.page.get_by_text("Display density", exact=False).count(),
            "Display density is back",
        )
        self.require(
            self.page.locator("html").get_attribute("data-mm-density") is None,
            "the page still carries a display density",
        )
        self.visible(
            self.page.get_by_test_id("suite-settings-upgrade-tab"), "Upgrade section"
        )
        self.click(
            self.page.get_by_role("button", name="Check again →", exact=True),
            "refresh upgrade status",
        )

        # Exercise the wizard's supported re-entry path, then leave it with the safe skip action so
        # the disposable audit account remains usable. It folds away because it is run once.
        self.click(
            self.page.get_by_role("heading", name="Setup wizard", exact=True),
            "open the Setup wizard group",
        )
        self.click(
            self.page.get_by_test_id("suite-settings-open-setup-wizard"),
            "open setup wizard from System",
        )
        self.visible(
            self.page.get_by_test_id("setup-wizard-skip"), "re-entered setup wizard"
        )
        self.visible(
            self.page.get_by_text("How do your downloads reach Weir?"),
            "re-entered setup wizard asks how downloads reach Weir",
        )
        self.require(
            not self.page.get_by_text("Display density", exact=False).count(),
            "Display density is back in the setup wizard",
        )
        self.click(
            self.page.get_by_test_id("setup-wizard-skip"),
            "skip re-entered setup wizard",
        )
        self.page.wait_for_url(
            lambda url: "/setup-wizard" not in url, timeout=TIMEOUT_MS
        )
        self.open_sidebar("System")
        self.visible(
            self.page.get_by_test_id("suite-settings-global"),
            "return to System › About",
        )
        self.screenshot("system-instance")
        self.record("System › About: time zone, runtime facts, upgrade refresh, and wizard re-entry")

    def system_backups_logs_security(self) -> None:
        self.open_tab("System", "Backups")
        self.visible(
            self.page.get_by_test_id("suite-settings-backup-tab"),
            "System backups panel",
        )
        self.require(
            self.page.get_by_role(
                "button", name="Download configuration now", exact=True
            ).count()
            > 0,
            "configuration download control missing",
        )
        with self.page.expect_download(timeout=TIMEOUT_MS) as download_info:
            self.page.get_by_role(
                "button", name="Download configuration now", exact=True
            ).click()
        self.require(
            download_info.value.suggested_filename.endswith(".json"),
            "configuration export is not JSON",
        )

        self.open_logs("Server log")
        logs = self.visible(
            self.page.get_by_test_id("suite-settings-logs"), "server log panel"
        )
        self.visible(
            logs.get_by_text("Server diagnostics", exact=True),
            "server diagnostics disclosure",
        )
        logs.get_by_placeholder(
            "Search message, detail, traceback, logger, or source"
        ).fill("audit")
        # Scoped to the log panel: the Show choice above it is a select too.
        level_select = logs.locator("select").first
        if level_select.count():
            level_select.select_option(index=1)
        toggles = logs.get_by_role("radio")
        if toggles.count() >= 2:
            toggles.last.click()
        refresh = logs.get_by_role("button", name="Refresh →", exact=True)
        if refresh.count():
            self.click(refresh, "refresh server log")

        self.open_tab("System", "Security")
        self.visible(
            self.page.get_by_test_id("suite-settings-security"),
            "System security panel",
        )
        self.visible(
            self.page.get_by_text("How sign-in is protected", exact=True), "how sign-in is protected"
        )
        self.visible(
            self.page.get_by_text("Active sessions", exact=True), "active sessions"
        )
        self.visible(
            self.page.get_by_role("heading", name="Change password", exact=True),
            "change-password controls",
        )
        self.screenshot("system-security")
        self.record(
            "System backup/export, server log filters, and security/session posture"
        )
