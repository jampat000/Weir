using Microsoft.Playwright;

namespace Weir.LiveAudit;

// Connections › Alerts (notification channels) and Connections › Media managers.
internal sealed partial class LiveAudit
{
    // The audit's media manager is named after its kind and address, not typed.
    private const string AuditManagerName = "Deluno on 127.0.0.1";

    private const string AuditChannelName = "Live audit channel";

    /// <summary>Runs <paramref name="action"/> and returns the first response to a request the predicate accepts.</summary>
    private Task<IResponse> ResponseDuringAsync(Func<Task> action, string method, string urlPart, string? urlEnd = null) =>
        page.RunAndWaitForResponseAsync(
            action,
            response => response.Request.Method == method
                && response.Url.Contains(urlPart, StringComparison.Ordinal)
                && (urlEnd is null || response.Url.EndsWith(urlEnd, StringComparison.Ordinal)),
            new PageRunAndWaitForResponseOptions { Timeout = AuditConfig.TimeoutMs });

    private ILocator ChannelRow() =>
        page.GetByText(AuditChannelName, new PageGetByTextOptions { Exact = true }).Locator("xpath=ancestor::tr");

    private static ILocator Button(ILocator scope, string name) =>
        scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = name, Exact = true });

    private ILocator PageButton(string name) =>
        page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = name, Exact = true });

    private async Task SettingsNotificationsAsync()
    {
        await OpenTabAsync("Connections", "Alerts");
        await VisibleAsync(page.GetByTestId("suite-settings-notifications"), "Alerts panel");
        // Make reruns safe after a diagnostic failure leaves the disposable channel behind.
        var existing = page.GetByText(AuditChannelName, new PageGetByTextOptions { Exact = true });
        while (await existing.CountAsync() > 0)
        {
            var card = existing.First.Locator("xpath=ancestor::tr");
            await ClickAsync(Button(card, "Remove"), "ask to remove leftover notification channel");
            var deleted = await ResponseDuringAsync(
                () => ConfirmRemovalAsync(
                    "notification-channel-remove-confirm",
                    AuditChannelName,
                    "remove leftover notification channel"),
                "DELETE",
                "/api/v1/suite/notification-channels/");
            Require(deleted.Status == 204, "leftover notification channel removal failed");
            await existing.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Detached,
                Timeout = AuditConfig.TimeoutMs,
            });
        }

        await ClickAsync(PageButton("Add alert"), "open notification channel form");
        var form = page.Locator("form")
            .Filter(new LocatorFilterOptions { Has = page.GetByText("Label", new PageGetByTextOptions { Exact = true }) })
            .Last;
        await VisibleAsync(form, "notification channel form");
        var labels = form.Locator("input[type='text']");
        Require(await labels.CountAsync() >= 1, "notification label control missing");
        await labels.First.FillAsync(AuditChannelName);
        await form.Locator("input[type='url']").FillAsync("https://example.invalid/webhook");
        var events = form.Locator("input[type='checkbox']");
        Require(await events.CountAsync() > 0, "notification event controls missing");
        // Keep the default failure event selected and exercise the enabled switch without leaving the final
        // channel disabled.
        await ClickAsync(Button(form, "Save alert"), "create notification channel");
        var row = page.GetByText(AuditChannelName, new PageGetByTextOptions { Exact = true });
        await VisibleAsync(row, "created notification channel");
        await ClickAsync(Button(ChannelRow(), "Edit"), "edit notification channel");
        await VisibleAsync(
            page.GetByText("Edit alert", new PageGetByTextOptions { Exact = true }),
            "notification edit form");
        await ClickAsync(PageButton("Cancel"), "cancel notification edit");
        // Cancelling has to leave the channel exactly where it was: that is the promise the confirmation makes, and
        // it is worth proving before relying on the confirm path.
        await ClickAsync(Button(ChannelRow(), "Remove"), "ask to remove notification channel");
        await ClickAsync(
            page.GetByTestId("notification-channel-remove-confirm-cancel"),
            "cancel notification channel removal");
        await VisibleAsync(
            page.GetByText(AuditChannelName, new PageGetByTextOptions { Exact = true }),
            "notification channel survived a cancelled removal");
        // Escape is the other way out, and it must not delete either.
        await ClickAsync(Button(ChannelRow(), "Remove"), "ask to remove notification channel again");
        await VisibleAsync(
            page.GetByTestId("notification-channel-remove-confirm"),
            "notification channel removal confirmation");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(120);
        await VisibleAsync(
            page.GetByText(AuditChannelName, new PageGetByTextOptions { Exact = true }),
            "notification channel survived Escape");
        await ClickAsync(Button(ChannelRow(), "Remove"), "ask to remove notification channel once more");
        var removed = await ResponseDuringAsync(
            () => ConfirmRemovalAsync("notification-channel-remove-confirm", AuditChannelName, "remove notification channel"),
            "DELETE",
            "/api/v1/suite/notification-channels/");
        Require(removed.Status == 204, "notification channel removal failed");
        await page.GetByText(AuditChannelName, new PageGetByTextOptions { Exact = true }).WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = AuditConfig.TimeoutMs });
        Record("notification channel create, edit/cancel, and confirmed remove");
    }

    private ILocator AuditManagerCards() =>
        page.GetByTestId("media-manager-card").Filter(new LocatorFilterOptions { HasText = AuditManagerName });

    private async Task SettingsMediaManagersAsync()
    {
        await OpenTabAsync("Connections", "Media managers");
        await VisibleAsync(page.GetByTestId("media-manager-add"), "Media managers panel");
        foreach (var leftover in await page.GetByTestId("media-manager-card").AllAsync())
        {
            if (!(await leftover.InnerTextAsync()).Contains(AuditManagerName, StringComparison.Ordinal))
            {
                continue;
            }

            await ClickAsync(leftover.GetByTestId("media-manager-remove"), "ask to remove leftover media manager");
            var deleted = await ResponseDuringAsync(
                () => ConfirmRemovalAsync("media-manager-remove-confirm", AuditManagerName, "remove leftover media manager"),
                "DELETE",
                "/api/v1/media-managers/connections/");
            Require(deleted.Status == 204, "leftover media manager removal failed");
            await leftover.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Detached,
                Timeout = AuditConfig.TimeoutMs,
            });
        }

        await ClickAsync(page.GetByTestId("media-manager-add"), "open media manager form");
        await page.GetByTestId("media-manager-base-url").FillAsync("http://127.0.0.1:9");
        await page.GetByTestId("media-manager-api-key").FillAsync("audit-secret");
        await ClickAsync(page.GetByTestId("media-manager-save"), "create media manager");
        var card = AuditManagerCards();
        await VisibleAsync(card, "created media manager");
        await VisibleAsync(card.GetByTestId("media-manager-status"), "media manager status");
        await ClickAsync(
            card.GetByTestId("media-manager-setup-details").Locator("summary"),
            "open media manager setup details");
        await ClickAsync(card.GetByTestId("media-manager-generate-secret"), "generate media manager webhook secret");
        await VisibleAsync(card.GetByTestId("media-manager-secret"), "generated webhook secret");
        await ClickAsync(Button(card, "Disable"), "disable media manager");
        await ClickAsync(Button(card, "Enable"), "enable media manager");
        var tested = await ResponseDuringAsync(
            () => ClickAsync(card.GetByTestId("media-manager-test"), "test media manager connection"),
            "POST",
            "/api/v1/media-managers/connections/",
            "/test");
        Require(tested.Status == 200, "media manager connection failure was not returned as a normal test result");
        await VisibleAsync(card.GetByTestId("media-manager-status"), "media manager test result");
        // Cancelling must leave the connection intact - the confirmation is only worth anything if "no" really
        // means nothing happened.
        await ClickAsync(card.GetByTestId("media-manager-remove"), "ask to remove media manager");
        await ClickAsync(page.GetByTestId("media-manager-remove-confirm-cancel"), "cancel media manager removal");
        await VisibleAsync(card, "media manager survived a cancelled removal");
        await ClickAsync(card.GetByTestId("media-manager-remove"), "ask to remove media manager again");
        var removed = await ResponseDuringAsync(
            () => ConfirmRemovalAsync("media-manager-remove-confirm", AuditManagerName, "remove media manager"),
            "DELETE",
            "/api/v1/media-managers/connections/");
        Require(removed.Status == 204, "media manager removal failed");
        Require(await AuditManagerCards().CountAsync() == 0, "media manager was not removed");
        await ScreenshotAsync("settings-integrations");
        Record("media-manager create, secret generation, enable/disable, connection test, and confirmed remove");
    }
}
