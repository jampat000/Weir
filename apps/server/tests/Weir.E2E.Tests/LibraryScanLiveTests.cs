using System.Globalization;
using System.Text.RegularExpressions;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

/// <summary>The Library screen follows a scan as the server runs it: queued, finished, and the counts it found, with no reload.</summary>
public sealed partial class LibraryScanLiveTests(E2EServer server) : E2ETestBase(server)
{
    private const int FilesInLibrary = 40;
    private const int FilesInLongLibrary = 30;
    private const double ProbeSeconds = 0.3;
    private const float ScanMs = 90_000;

    [GeneratedRegex(@"^([\d,]+) files · ")]
    private static partial Regex FilesCount();

    [GeneratedRegex(@"· ([\d,]+) files so far")]
    private static partial Regex FilesSoFar();

    [E2EFact]
    public async Task A_scan_started_through_the_api_shows_as_checking_and_then_as_the_counts_it_found_without_a_reload()
    {
        using var folders = new TemporaryFolder();
        var libraryFolder = Directory.CreateDirectory(Path.Join(folders.Path, "library")).FullName;
        for (var index = 0; index < FilesInLibrary; index++)
        {
            await File.WriteAllBytesAsync(Path.Join(libraryFolder, $"Film {index:D3}.mkv"), [1, 2, 3, 4]);
        }

        var page = await NewPageAsync();
        await Navigation.EnsureSignedInAsync(page, BaseUrl);
        var context = page.Context;
        var id = await WeirWorkflowApi.CreateAsync(
            context,
            BaseUrl,
            "E2E scanned films",
            Directory.CreateDirectory(Path.Join(folders.Path, "watched")).FullName,
            Directory.CreateDirectory(Path.Join(folders.Path, "output")).FullName);
        try
        {
            await WeirWorkflowApi.SetLibraryFoldersAsync(context, BaseUrl, id, libraryFolder);
            await page.GotoAsync($"{BaseUrl}/library?library={id}");
            await page.EvaluateAsync("() => { window.__weirNotReloaded = true; }");
            var scan = page.GetByTestId("library-scan");
            await Expect(scan).ToContainTextAsync("not scanned yet", new() { Timeout = Navigation.UrlAssertMs });

            // Paused, a scan waits in the queue, so the screen has a moment of "checking" to show.
            await WeirApi.SetPausedAsync(context, BaseUrl, paused: true);
            await WeirWorkflowApi.StartLibraryScanAsync(context, BaseUrl, id);
            await Expect(scan).ToContainTextAsync("Checking this workflow now");

            await WeirApi.SetPausedAsync(context, BaseUrl, paused: false);

            var counts = await ObserveCountsUntilAsync(page, FilesInLibrary);
            await Expect(scan).ToContainTextAsync("checked just now", new() { Timeout = ScanMs });
            await Expect(scan).Not.ToContainTextAsync("Checking this workflow now");
            Assert.Equal(FilesInLibrary, counts[^1]);
            Assert.Equal(counts.Order(), counts);
            Assert.True(await page.EvaluateAsync<bool>("() => window.__weirNotReloaded === true"), "The page was reloaded.");
        }
        finally
        {
            await WeirApi.SetPausedAsync(context, BaseUrl, paused: false);
            await WeirWorkflowApi.DeleteAsync(context, BaseUrl, id);
        }
    }

    [E2EFact]
    public async Task A_long_scan_counts_up_the_files_it_has_looked_at_before_it_reports_what_it_found()
    {
        using var folders = new TemporaryFolder();
        var libraryFolder = Directory.CreateDirectory(Path.Join(folders.Path, "library")).FullName;
        for (var index = 0; index < FilesInLongLibrary; index++)
        {
            await File.WriteAllBytesAsync(Path.Join(libraryFolder, $"Film {index:D4}.mkv"), [1, 2, 3, 4]);
        }

        // Each file takes the fake ffprobe a set time to answer, so the walk lasts several progress intervals on any machine.
        using var tools = FakeFfmpeg.Install();
        tools.SetFileRule("*.mkv", new FileRule { ProbeDelaySeconds = ProbeSeconds });
        await using var own = await E2EServer.StartWithFakeToolsAsync(tools);
        var baseUrl = own.BaseUrl.GetLeftPart(UriPartial.Authority);

        var page = await NewPageAsync();
        await Navigation.EnsureSignedInAsync(page, baseUrl);
        var context = page.Context;
        var id = await WeirWorkflowApi.CreateAsync(
            context,
            baseUrl,
            "E2E long scan",
            Directory.CreateDirectory(Path.Join(folders.Path, "watched")).FullName,
            Directory.CreateDirectory(Path.Join(folders.Path, "output")).FullName);
        await WeirWorkflowApi.SetLibraryFoldersAsync(context, baseUrl, id, libraryFolder);
        await page.GotoAsync($"{baseUrl}/library?library={id}");
        await page.EvaluateAsync("() => { window.__weirNotReloaded = true; }");
        var scan = page.GetByTestId("library-scan");
        await Expect(scan).ToContainTextAsync("not scanned yet", new() { Timeout = Navigation.UrlAssertMs });

        await WeirWorkflowApi.StartLibraryScanAsync(context, baseUrl, id);

        var counted = await ObserveFilesSoFarUntilCheckedAsync(page);
        Assert.True(counted.Count >= 3, $"The scan showed only {string.Join(", ", counted)} files so far.");
        Assert.All(counted, seen => Assert.InRange(seen, 1, FilesInLongLibrary));
        Assert.Equal(counted.Order(), counted);
        await Expect(page.GetByText(FilesCount())).ToContainTextAsync($"{FilesInLongLibrary:N0} files", new() { Timeout = ScanMs });
        Assert.True(await page.EvaluateAsync<bool>("() => window.__weirNotReloaded === true"), "The page was reloaded.");
    }

    /// <summary>Every distinct "files so far" count the scan line shows, in order, until the scan reports itself checked.</summary>
    private static async Task<List<int>> ObserveFilesSoFarUntilCheckedAsync(Microsoft.Playwright.IPage page)
    {
        var seen = new List<int>();
        var deadline = DateTime.UtcNow.AddMilliseconds(ScanMs);
        var line = page.GetByTestId("library-scan");
        while (DateTime.UtcNow < deadline)
        {
            var text = await line.InnerTextAsync();
            if (FilesSoFar().Match(text) is { Success: true } match)
            {
                var shown = int.Parse(match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
                if (seen.Count == 0 || seen[^1] != shown)
                {
                    seen.Add(shown);
                }
            }
            else if (text.Contains("checked", StringComparison.Ordinal))
            {
                return seen;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"The scan never finished; it showed {string.Join(", ", seen)} files so far.");
    }

    /// <summary>Every distinct file count the Files card shows, in the order it shows them, until it shows <paramref name="expected"/>.</summary>
    private static async Task<List<int>> ObserveCountsUntilAsync(Microsoft.Playwright.IPage page, int expected)
    {
        var seen = new List<int>();
        var deadline = DateTime.UtcNow.AddMilliseconds(ScanMs);
        var count = page.GetByText(FilesCount());
        while (DateTime.UtcNow < deadline)
        {
            if (await count.CountAsync() > 0)
            {
                var text = await count.First.InnerTextAsync();
                var shown = int.Parse(FilesCount().Match(text).Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
                if (seen.Count == 0 || seen[^1] != shown)
                {
                    seen.Add(shown);
                }

                if (shown == expected)
                {
                    return seen;
                }
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"The Files card never showed {expected} files; it showed {string.Join(", ", seen)}.");
    }
}
