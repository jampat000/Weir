using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Weir.Contract.Tests.Harness;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

/// <summary>Dashboard › System follows Weir's own look at a workflow's folders: it turns red when they go and green when they are back, with no reload.</summary>
public sealed partial class SystemHealthLiveTests(E2EServer server) : E2ETestBase(server)
{
    [GeneratedRegex(@"checked (just now|(\d+)s ago|[^·]*ago)")]
    private static partial Regex CheckedAgo();

    // The server looks at the folders every 15 s while a browser watches, and the page reads the answer when it is told.
    private const float FolderCheckMs = 60_000;

    [E2EFact]
    public async Task A_workflow_whose_folder_goes_missing_turns_red_and_green_again_when_it_is_back_without_a_reload()
    {
        using var folders = new TemporaryFolder();
        var watched = Directory.CreateDirectory(Path.Join(folders.Path, "watched")).FullName;
        var output = Directory.CreateDirectory(Path.Join(folders.Path, "output")).FullName;
        var page = await NewPageAsync();
        await Navigation.EnsureSignedInAsync(page, BaseUrl);
        var id = await WeirWorkflowApi.CreateAsync(page.Context, BaseUrl, "E2E checked films", watched, output);
        try
        {
            await page.GotoAsync($"{BaseUrl}/?view=system");
            await page.EvaluateAsync("() => { window.__weirNotReloaded = true; }");
            await page.GetByRole(AriaRole.Group, new() { Name = "Health areas" }).GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Workflows") }).ClickAsync();
            var row = page.GetByTestId("system-check").Filter(new() { HasText = "E2E checked films" });
            await Expect(row).ToHaveAttributeAsync("data-status", "done", new() { Timeout = FolderCheckMs });

            // Unreachable, the way a share that drops off the network is: the folder is simply not there.
            Directory.Delete(watched);
            await Expect(row).Not.ToHaveAttributeAsync("data-status", "done", new() { Timeout = FolderCheckMs });
            await Expect(row).ToContainTextAsync("needs a fix");

            Directory.CreateDirectory(watched);
            await Expect(row).ToHaveAttributeAsync("data-status", "done", new() { Timeout = FolderCheckMs });
            await Expect(row).Not.ToContainTextAsync("needs a fix");

            Assert.True(await page.EvaluateAsync<bool>("() => window.__weirNotReloaded === true"), "The page was reloaded.");
        }
        finally
        {
            // A job of the workflow cannot finish while its folder is gone, and Weir will not remove a workflow with a job left.
            Directory.CreateDirectory(watched);
            await WeirWorkflowApi.DeleteAsync(page.Context, BaseUrl, id);
        }
    }

    [E2EFact]
    public async Task A_workflows_checked_time_is_the_servers_last_look_not_the_pages_last_read_and_a_drive_says_live()
    {
        using var folders = new TemporaryFolder();
        var page = await NewPageAsync();
        await Navigation.EnsureSignedInAsync(page, BaseUrl);
        var id = await WeirWorkflowApi.CreateAsync(
            page.Context,
            BaseUrl,
            "E2E looked-at films",
            Directory.CreateDirectory(Path.Join(folders.Path, "watched")).FullName,
            Directory.CreateDirectory(Path.Join(folders.Path, "output")).FullName);
        try
        {
            await page.GotoAsync($"{BaseUrl}/?view=system");

            // The card lists only the rows that fit, problems first, and a hidden row has no text on screen. Among every check this
            // row can fall below a missing tool and be left out, as it is on a machine without FFmpeg; under Workflows it is not.
            await page.GetByRole(AriaRole.Group, new() { Name = "Health areas" }).GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Workflows") }).ClickAsync();
            var row = page.GetByTestId("system-check").Filter(new() { HasText = "E2E looked-at films" });
            await Expect(row).ToHaveAttributeAsync("data-status", "done", new() { Timeout = FolderCheckMs });
            await Expect(row).ToBeVisibleAsync();
            await Expect(row).ToContainTextAsync("checked");

            // Nothing about the folders changes, so the page reads nothing new; the server looks every 15 s all the same.
            await Task.Delay(TimeSpan.FromSeconds(40));

            await Expect(row).ToBeVisibleAsync();
            var shown = CheckedAgo().Match(await row.InnerTextAsync());
            Assert.True(shown.Success, "The row did not say when it was checked.");
            Assert.True(
                shown.Groups[1].Value == "just now" || (shown.Groups[2].Success && int.Parse(shown.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) <= 20),
                $"After 40 s of nothing changing the row said \"checked {shown.Groups[1].Value}\", but the server looks every 15 s.");

            await page.GetByRole(AriaRole.Group, new() { Name = "Health areas" }).GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Storage") }).ClickAsync();
            await Expect(page.GetByTestId("system-check").First).ToContainTextAsync("live");
        }
        finally
        {
            await WeirWorkflowApi.DeleteAsync(page.Context, BaseUrl, id);
        }
    }
}
