using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

/// <summary>System › Logs follows the job queue: a job queued through the API shows up in the open log, with no reload.</summary>
public sealed class SystemLogsLiveTests(E2EServer server) : E2ETestBase(server)
{
    [E2EFact]
    public async Task A_job_queued_through_the_api_appears_in_the_open_log_without_a_reload()
    {
        const string marker = "live-marker-clip";
        var folders = Directory.CreateTempSubdirectory("weir_e2e_live_logs_");
        try
        {
            var watched = Directory.CreateDirectory(Path.Join(folders.FullName, "watched")).FullName;
            var output = Directory.CreateDirectory(Path.Join(folders.FullName, "output")).FullName;
            var page = await NewPageAsync();
            await Navigation.EnsureSignedInAsync(page, BaseUrl);
            await WeirApi.SetMovieFoldersAsync(page.Context, BaseUrl, watched, output);
            await Navigation.OpenLogsAsync(page, "Jobs");
            await page.EvaluateAsync("() => { window.__weirNotReloaded = true; }");
            var row = page.GetByText($"Finished for {marker}.mkv", new() { Exact = true });
            await Expect(page.GetByText(marker).First).ToHaveCountAsync(0);

            await WeirApi.EnqueueFileAsync(page.Context, BaseUrl, $"{marker}.mkv");

            await Expect(row).ToBeVisibleAsync(new() { Timeout = 10_000 });
            Assert.True(await page.EvaluateAsync<bool>("() => window.__weirNotReloaded === true"), "The page was reloaded.");
        }
        finally
        {
            folders.Delete(recursive: true);
        }
    }
}
