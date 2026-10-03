using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

public sealed class AppNavigationTests(E2EServer server) : E2ETestBase(server)
{
    private sealed record SetupTab(string Label, string Address, string Section);

    private sealed record SetupArea(string Label, string Address, SetupTab[] Tabs);

    private sealed record Landing(string From, string To, string Section);

    // Setup area (an entry of its own in the side menu) -> its address, and each of its tabs with the address and the
    // content it shows. The first tab is at the area's own address. Weir itself is System, not Setup.
    private static readonly SetupArea[] SetupAreas =
    [
        new("Workflows", "/setup/workflows",
        [
            new("File paths", "/setup/workflows", "processing-libraries-section"),
            new("Schedule", "/setup/workflows/schedule", "processing-schedules-section"),
        ]),
        new("Connections", "/setup/connections",
        [
            new("Media managers", "/setup/connections", "suite-settings-media-managers"),
            new("Download clients", "/setup/connections/download-clients", "suite-settings-download-clients-tab"),
            new("Alerts", "/setup/connections/alerts", "suite-settings-notifications"),
        ]),
        new("Rules", "/setup/rules",
        [
            new("Profiles", "/setup/rules", "processing-rule-set-workspace"),
            new("Playback devices", "/setup/rules/devices", "processing-direct-play-section"),
        ]),
        new("Performance", "/setup/performance",
        [
            new("Speed", "/setup/performance", "processing-process-settings"),
            new("Cleanup", "/setup/performance/cleanup", "processing-maintenance-section"),
            new("Weir's timers", "/setup/performance/timers", "processing-timers-section"),
        ]),
    ];

    // Former Settings addresses, and the setup tab each one lands on.
    private static readonly Landing[] FormerSettingsAddresses =
    [
        new("/settings", "/setup/workflows", "processing-libraries-section"),
        new("/settings?tab=libraries", "/setup/workflows", "processing-libraries-section"),
        new("/settings?tab=rules", "/setup/rules", "processing-rule-set-workspace"),
        new("/settings?tab=media-managers", "/setup/connections", "suite-settings-media-managers"),
        new("/settings?tab=performance", "/setup/performance", "processing-process-settings"),
        new("/settings?tab=schedule", "/setup/workflows/schedule", "processing-schedules-section"),
        new("/settings?tab=cleanup", "/setup/performance/cleanup", "processing-maintenance-section"),
        new("/settings?tab=alerts", "/setup/connections/alerts", "suite-settings-notifications"),
    ];

    // Addresses users may have saved (the Processing tabs) land on the same thing in its current place.
    private static readonly (string OldTab, string NewAddress, string Section)[] FormerProcessingTabs =
    [
        ("jobs", @"/system\?tab=logs&source=job", "log-feed"),
        ("files", @"/activity", "activity-page"),
        ("libraries", @"/setup/workflows", "processing-libraries-section"),
        ("audio-subtitles", @"/setup/rules", "processing-rule-set-workspace"),
        ("schedules", @"/setup/workflows/schedule", "processing-schedules-section"),
        ("maintenance", @"/setup/performance/cleanup", "processing-maintenance-section"),
    ];

    [E2EFact]
    public async Task Signed_in_navigation_covers_main_screens_and_tabs()
    {
        var page = await NewPageAsync();
        var domContentLoaded = new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded };

        await Navigation.EnsureSignedInAsync(page, BaseUrl);

        // The Dashboard (the landing screen) and Activity, Library, each setup area, then System.
        var primary = page.GetByRole(AriaRole.Navigation, new() { Name = "Primary" });
        // The labels, not the links: the Dashboard link also carries its "1 working" badge while a file runs.
        await Expect(primary.Locator(".mm-sidebar-link-label")).ToHaveTextAsync(
            ["Dashboard", "Activity", "Library", .. SetupAreas.Select(area => area.Label), "System"]);
        foreach (var retired in new[] { "Home", "Processing", "History" })
        {
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = retired, Exact = true })).ToHaveCountAsync(0);
        }

        await Navigation.OpenSidebarAsync(page, "Dashboard");
        await Expect(page).ToHaveURLAsync(new Regex(@".*/(?:$|[?#])"));
        await Expect(page.GetByTestId("processing-page")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
        // A retired address gets the not-found page, not a hidden alias.
        await page.GotoAsync($"{BaseUrl}/dashboard", domContentLoaded);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "This page doesn't exist.", Exact = true })).ToBeVisibleAsync();
        await page.GotoAsync($"{BaseUrl}/", domContentLoaded);
        await Expect(page.GetByTestId("processing-page")).ToBeVisibleAsync();

        // Every file Weir has touched, with what it kept and removed.
        await Navigation.OpenSidebarAsync(page, "Activity");
        await Expect(page).ToHaveURLAsync(new Regex(@".*/activity(?:$|[?#])"));
        await Expect(page.GetByTestId("activity-page")).ToBeVisibleAsync();
        // Needs you is every file waiting on a person, as the sidebar's badge counts them, in whichever group it
        // is; a skip is its own neutral group rather than counting as Failed, on_hold sits under On hold, and
        // Kept lists the files the owner chose to keep without processing.
        // At Playwright's 1280x720 the last chips may have folded into More, so the whole set is the chips on the
        // title line plus the menu's entries.
        Assert.Equal(
            ["All", "In progress", "Finished", "Needs you", "On hold", "Skipped", "Failed", "Kept"],
            await Navigation.ActivityChipLabelsAsync(page));

        // History was this page's name: an old link or bookmark lands here with its filters.
        await page.GotoAsync($"{BaseUrl}/history?show=failed&within=all", domContentLoaded);
        await Expect(page).ToHaveURLAsync(new Regex(@".*/activity\?show=failed&within=all$"));
        await Expect(page.GetByTestId("activity-page")).ToBeVisibleAsync();

        await Navigation.OpenSidebarAsync(page, "Library");
        await Expect(page).ToHaveURLAsync(new Regex(@".*/library(?:$|[?#])"));
        await Expect(page.GetByTestId("library-page")).ToBeVisibleAsync();

        // Each setup area has a row of tabs of its own: the side menu is the way between areas, the tabs are the
        // way within one, and the header names the area showing. Every tab has its own address.
        foreach (var area in SetupAreas)
        {
            await Navigation.OpenSidebarAsync(page, area.Label);
            await Expect(page).ToHaveURLAsync(EndingWith(area.Address));
            await Expect(page.GetByTestId("suite-settings-page")).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = area.Label, Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByTestId("setup-area-tabs").GetByRole(AriaRole.Tab)).ToHaveTextAsync(area.Tabs.Select(tab => tab.Label).ToArray());
            foreach (var tab in area.Tabs)
            {
                var tabButton = page.GetByRole(AriaRole.Tab, new() { Name = tab.Label, Exact = true });
                await tabButton.ClickAsync();
                await Expect(page).ToHaveURLAsync(EndingWith(tab.Address));
                await Expect(page.GetByRole(AriaRole.Tab, new() { Name = tab.Label, Exact = true })).ToHaveAttributeAsync("aria-selected", "true");
                await Expect(page.GetByTestId(tab.Section)).ToBeVisibleAsync();
            }
        }

        await Expect(page.GetByTestId("settings-section-tabs")).ToHaveCountAsync(0);

        await Navigation.OpenSidebarAsync(page, "System");
        await Expect(page).ToHaveURLAsync(new Regex(@".*/system(?:$|[?#])"));
        await Expect(page.GetByTestId("suite-system-page")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("system-section-tabs").GetByRole(AriaRole.Tab)).ToHaveTextAsync(["About", "Backups", "Security", "Logs"]);
        await Expect(page.GetByTestId("suite-settings-global")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("suite-settings-open-setup-wizard")).ToBeVisibleAsync();
        // The time zone is Weir-wide, so it is set here, in the "This PC" card.
        await Expect(page.GetByText("Time zone", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Updates", new() { Exact = true })).ToBeVisibleAsync();
        // There is no display density setting.
        await Expect(page.GetByText("Display density", new() { Exact = false })).ToHaveCountAsync(0);
        await Expect(page.Locator("html")).Not.ToHaveAttributeAsync("data-mm-density", new Regex(".*"));

        await Navigation.OpenTabAsync(page, "System", "Security");
        await Expect(page.GetByTestId("suite-settings-security")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Change password", Exact = true })).ToBeVisibleAsync();

        await Navigation.OpenLogsAsync(page);
        await Expect(page).ToHaveURLAsync(new Regex(@".*/system\?tab=logs(?:$|[&#])"));
        await Expect(page.GetByTestId("log-feed")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("log-summary")).ToContainTextAsync("entries");
        // Weir is one app: no Module filter, and no Apply button: a filter applies as it is chosen.
        await Expect(page.GetByText("All modules", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true })).ToHaveCountAsync(0);

        // Events, jobs and the server log are one list, told apart by the Source chips.
        await Navigation.OpenLogsAsync(page, "Jobs");
        await Expect(page).ToHaveURLAsync(new Regex(@".*/system\?tab=logs&source=job"));
        await Expect(page.GetByTestId("log-feed")).ToBeVisibleAsync();

        await Navigation.OpenLogsAsync(page, "Server");
        await Expect(page.GetByTestId("log-feed")).ToBeVisibleAsync();
        await Expect(page.GetByTestId("logs-export")).ToBeVisibleAsync();

        // How long things are kept, and the server's counters, are in the Log settings the Log card opens.
        await Expect(page.GetByText("Server diagnostics", new() { Exact = true })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log settings", Exact = true }).ClickAsync();
        var settings = page.GetByRole(AriaRole.Dialog, new() { Name = "Log settings" });
        await Expect(settings.GetByText("Server diagnostics", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(settings.GetByText("How long things are kept", new() { Exact = true })).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(settings).ToHaveCountAsync(0);

        // The address Logs had for its server list lands on the Server source.
        await page.GotoAsync($"{BaseUrl}/system?tab=logs&show=server", domContentLoaded);
        var serverChip = page.GetByRole(AriaRole.Group, new() { Name = "Source" })
            .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Server") });
        await Expect(serverChip).ToHaveAttributeAsync("aria-pressed", "true");

        foreach (var (oldTab, newAddress, section) in FormerProcessingTabs)
        {
            await page.GotoAsync($"{BaseUrl}/processing?tab={oldTab}", domContentLoaded);
            await Expect(page).ToHaveURLAsync(new Regex($".*{newAddress}$"));
            await Expect(page.GetByTestId(section)).ToBeVisibleAsync();
        }

        // So do the Settings addresses, which are the setup areas now.
        foreach (var former in FormerSettingsAddresses)
        {
            await page.GotoAsync($"{BaseUrl}{former.From}", domContentLoaded);
            await Expect(page).ToHaveURLAsync(EndingWith(former.To));
            await Expect(page.GetByTestId(former.Section)).ToBeVisibleAsync();
        }
    }

    private static Regex EndingWith(string address) => new($".*{address}$");
}
