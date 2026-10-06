using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

public sealed class ActivityLiveRefreshTests(E2EServer server) : E2ETestBase(server)
{
    [E2EFact]
    public async Task The_log_updates_without_manual_refresh()
    {
        const string markerDetail = "Live refresh reached the open Logs tab.";
        var page = await NewPageAsync();

        await Navigation.EnsureSignedInAsync(page, BaseUrl);
        await Navigation.OpenLogsAsync(page);

        await Expect(page.GetByText("Password changed", new() { Exact = true })).ToHaveCountAsync(0);

        E2EDatabase.InsertActivityEvent(
            Server.DatabasePath,
            eventType: "auth.password_changed",
            module: "auth",
            title: "Live refresh marker event",
            detail: markerDetail);

        // The row arrives on its own; it opens to what the event recorded.
        var title = page.GetByText("Password changed", new() { Exact = true });
        await Expect(title).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(page.GetByText("Account and sign-in activity", new() { Exact = true }).First).ToBeVisibleAsync();
        await title.ClickAsync();
        await Expect(page.GetByText(markerDetail, new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 10_000 });
    }
}
