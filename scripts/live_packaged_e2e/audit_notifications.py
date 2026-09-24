"""``AuditNotificationsMixin``: Settings › Alerts (notification channels) and Settings › Media
managers. Assumes ``AuditCore`` and ``AuditShellMixin`` in the same instance.
"""

from __future__ import annotations

from .config import TIMEOUT_MS


class AuditNotificationsMixin:
    def settings_notifications(self) -> None:
        self.open_tab("Settings", "Alerts")
        self.visible(
            self.page.get_by_test_id("suite-settings-notifications"),
            "Settings alerts panel",
        )
        # Make reruns safe after a diagnostic failure leaves the disposable
        # channel behind.
        existing = self.page.get_by_text("Live audit channel", exact=True)
        while existing.count():
            card = existing.first.locator("xpath=ancestor::tr")
            self.click(
                card.get_by_role("button", name="Remove", exact=True),
                "ask to remove leftover notification channel",
            )
            with self.page.expect_response(
                lambda response: (
                    response.request.method == "DELETE"
                    and "/api/v1/suite/notification-channels/" in response.url
                ),
                timeout=TIMEOUT_MS,
            ) as delete_response:
                self.confirm_removal(
                    "notification-channel-remove-confirm",
                    "Live audit channel",
                    "remove leftover notification channel",
                )
            self.require(
                delete_response.value.status == 204,
                "leftover notification channel removal failed",
            )
            existing.first.wait_for(state="detached", timeout=TIMEOUT_MS)
        self.click(
            self.page.get_by_role(
                "button", name="Add an alert →", exact=True
            ),
            "open notification channel form",
        )
        form = (
            self.page.locator("form")
            .filter(has=self.page.get_by_text("Label", exact=True))
            .last
        )
        self.visible(form, "notification channel form")
        labels = form.locator("input[type='text']")
        self.require(labels.count() >= 1, "notification label control missing")
        labels.first.fill("Live audit channel")
        form.locator("input[type='url']").fill("https://example.invalid/webhook")
        events = form.locator("input[type='checkbox']")
        self.require(events.count() > 0, "notification event controls missing")
        # Keep the default failure event selected and exercise the enabled switch
        # without leaving the final channel disabled.
        self.click(
            form.get_by_role("button", name="Save alert", exact=True),
            "create notification channel",
        )
        row = self.page.get_by_text("Live audit channel", exact=True)
        self.visible(row, "created notification channel")
        row_parent = row.locator("xpath=ancestor::tr")
        self.click(
            row_parent.get_by_role("button", name="Edit", exact=True),
            "edit notification channel",
        )
        self.visible(
            self.page.get_by_text("Edit alert", exact=True), "notification edit form"
        )
        self.click(
            self.page.get_by_role("button", name="Cancel", exact=True),
            "cancel notification edit",
        )
        row_parent = self.page.get_by_text("Live audit channel", exact=True).locator(
            "xpath=ancestor::tr"
        )
        # Cancelling has to leave the channel exactly where it was: that is the promise the
        # confirmation makes, and it is worth proving before relying on the confirm path.
        self.click(
            row_parent.get_by_role("button", name="Remove", exact=True),
            "ask to remove notification channel",
        )
        self.click(
            self.page.get_by_test_id("notification-channel-remove-confirm-cancel"),
            "cancel notification channel removal",
        )
        self.visible(
            self.page.get_by_text("Live audit channel", exact=True),
            "notification channel survived a cancelled removal",
        )
        row_parent = self.page.get_by_text("Live audit channel", exact=True).locator(
            "xpath=ancestor::tr"
        )
        # Escape is the other way out, and it must not delete either.
        self.click(
            row_parent.get_by_role("button", name="Remove", exact=True),
            "ask to remove notification channel again",
        )
        self.visible(
            self.page.get_by_test_id("notification-channel-remove-confirm"),
            "notification channel removal confirmation",
        )
        self.page.keyboard.press("Escape")
        self.page.wait_for_timeout(120)
        self.visible(
            self.page.get_by_text("Live audit channel", exact=True),
            "notification channel survived Escape",
        )
        row_parent = self.page.get_by_text("Live audit channel", exact=True).locator(
            "xpath=ancestor::tr"
        )
        self.click(
            row_parent.get_by_role("button", name="Remove", exact=True),
            "ask to remove notification channel once more",
        )
        with self.page.expect_response(
            lambda response: (
                response.request.method == "DELETE"
                and "/api/v1/suite/notification-channels/" in response.url
            ),
            timeout=TIMEOUT_MS,
        ) as delete_response:
            self.confirm_removal(
                "notification-channel-remove-confirm",
                "Live audit channel",
                "remove notification channel",
            )
        self.require(
            delete_response.value.status == 204, "notification channel removal failed"
        )
        self.page.get_by_text("Live audit channel", exact=True).wait_for(
            state="detached", timeout=TIMEOUT_MS
        )
        self.record("notification channel create, edit/cancel, and confirmed remove")

    def settings_media_managers(self) -> None:
        self.click(
            self.page.get_by_role("tab", name="Media managers", exact=True),
            "open Settings media managers",
        )
        self.visible(
            self.page.get_by_test_id("media-manager-add"),
            "Settings media managers panel",
        )
        for card in self.page.get_by_test_id("media-manager-card").all():
            if "Live audit manager" in card.inner_text():
                self.click(
                    card.get_by_test_id("media-manager-remove"),
                    "ask to remove leftover media manager",
                )
                with self.page.expect_response(
                    lambda response: (
                        response.request.method == "DELETE"
                        and "/api/v1/media-managers/connections/" in response.url
                    ),
                    timeout=TIMEOUT_MS,
                ) as delete_response:
                    self.confirm_removal(
                        "media-manager-remove-confirm",
                        "Live audit manager",
                        "remove leftover media manager",
                    )
                self.require(
                    delete_response.value.status == 204,
                    "leftover media manager removal failed",
                )
                card.wait_for(state="detached", timeout=TIMEOUT_MS)
        self.click(
            self.page.get_by_test_id("media-manager-add"), "open media manager form"
        )
        self.page.get_by_test_id("media-manager-name").fill("Live audit manager")
        self.page.get_by_test_id("media-manager-base-url").fill("http://127.0.0.1:9")
        self.page.get_by_test_id("media-manager-api-key").fill("audit-secret")
        self.click(
            self.page.get_by_test_id("media-manager-save"), "create media manager"
        )
        card = self.page.get_by_test_id("media-manager-card").filter(
            has_text="Live audit manager"
        )
        self.visible(card, "created media manager")
        self.visible(
            card.get_by_test_id("media-manager-status"), "media manager status"
        )
        self.click(
            card.get_by_test_id("media-manager-setup-details").locator("summary"),
            "open media manager setup details",
        )
        self.click(
            card.get_by_test_id("media-manager-generate-secret"),
            "generate media manager webhook secret",
        )
        self.visible(
            card.get_by_test_id("media-manager-secret"), "generated webhook secret"
        )
        self.click(
            card.get_by_role("button", name="Disable", exact=True),
            "disable media manager",
        )
        self.click(
            card.get_by_role("button", name="Enable", exact=True),
            "enable media manager",
        )
        with self.page.expect_response(
            lambda response: (
                response.request.method == "POST"
                and "/api/v1/media-managers/connections/" in response.url
                and response.url.endswith("/test")
            ),
            timeout=TIMEOUT_MS,
        ) as test_response:
            self.click(
                card.get_by_test_id("media-manager-test"),
                "test media manager connection",
            )
        self.require(
            test_response.value.status == 200,
            "media manager connection failure was not returned as a normal test result",
        )
        self.visible(
            card.get_by_test_id("media-manager-status"), "media manager test result"
        )
        # Cancelling must leave the connection intact - the confirmation is only worth
        # anything if "no" really means nothing happened.
        self.click(
            card.get_by_test_id("media-manager-remove"),
            "ask to remove media manager",
        )
        self.click(
            self.page.get_by_test_id("media-manager-remove-confirm-cancel"),
            "cancel media manager removal",
        )
        self.visible(card, "media manager survived a cancelled removal")
        self.click(
            card.get_by_test_id("media-manager-remove"),
            "ask to remove media manager again",
        )
        with self.page.expect_response(
            lambda response: (
                response.request.method == "DELETE"
                and "/api/v1/media-managers/connections/" in response.url
            ),
            timeout=TIMEOUT_MS,
        ) as delete_response:
            self.confirm_removal(
                "media-manager-remove-confirm",
                "Live audit manager",
                "remove media manager",
            )
        self.require(
            delete_response.value.status == 204, "media manager removal failed"
        )
        self.require(
            not self.page.get_by_test_id("media-manager-card")
            .filter(has_text="Live audit manager")
            .count(),
            "media manager was not removed",
        )
        self.screenshot("settings-integrations")
        self.record(
            "media-manager create, secret generation, enable/disable, connection test, and confirmed remove"
        )
