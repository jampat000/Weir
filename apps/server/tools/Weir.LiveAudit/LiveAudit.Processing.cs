using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Weir.LiveAudit;

// The Processing pass-through lifecycle proof (a real FFmpeg fixture, optional), then setup-area and System URL
// history and the not-found route.
internal sealed partial class LiveAudit
{
    /// <summary>Proves an unchanged file reaches output before its watched source is removed.</summary>
    private async Task ProcessingPassThroughLifecycleAsync()
    {
        var configured = config.FixtureHostRoot.Length > 0 || config.FixtureServerRoot.Length > 0;
        if (!configured)
        {
            Record("Processing pass-through lifecycle skipped (no controlled fixture mount)");
            return;
        }

        Require(
            config.FixtureHostRoot.Length > 0 && config.FixtureServerRoot.Length > 0,
            "both Processing fixture host and server roots are required");
        Require(config.FixtureFfmpeg.Length > 0, "Processing fixture FFmpeg command is required");

        var fixture = PassThroughFixture.Create(config.FixtureHostRoot, config.FixtureServerRoot);
        await fixture.CreateSourceAsync(config.FixtureFfmpeg);
        Require(File.Exists(fixture.Source), "FFmpeg did not create the pass-through fixture");
        var oldTime = DateTime.UtcNow.AddSeconds(-600);
        File.SetLastWriteTimeUtc(fixture.Source, oldTime);
        File.SetLastAccessTimeUtc(fixture.Source, oldTime);
        var sourceSize = new FileInfo(fixture.Source).Length;
        var sourceHash = PassThroughFixture.Sha256(fixture.Source);

        // The seeded Movies library is configured directly; the path-settings route was retired in #460.
        var libraries = await BrowserApiAsync("GET", "/api/v1/processing/libraries");
        var movies = (libraries.Payload as JsonArray)?
            .OfType<JsonObject>()
            .FirstOrDefault(row => row["media_type"]?.GetValue<string>() == "movie");
        Require(movies is not null, "the install has no Movies library to configure");
        var pathResult = await BrowserApiAsync(
            "PUT",
            $"/api/v1/processing/libraries/{movies!["id"]}",
            new Dictionary<string, object?>
            {
                ["csrf_token"] = await CsrfTokenAsync(),
                ["name"] = movies["name"]?.GetValue<string>(),
                ["media_type"] = "movie",
                ["watched_folder"] = fixture.ServerFolder("watch"),
                ["work_folder"] = fixture.ServerFolder("work"),
                ["output_folder"] = fixture.ServerFolder("processed"),
                ["manager_connection_ids"] = ManagerConnectionIds(movies),
            });
        Require(pathResult.Status == 200, $"could not configure pass-through fixture paths: {pathResult.Payload?.ToJsonString()}");
        var enqueue = await BrowserApiAsync(
            "POST",
            "/api/v1/processing/jobs/file-remux-pass/enqueue",
            new Dictionary<string, object?>
            {
                ["csrf_token"] = await CsrfTokenAsync(),
                ["relative_media_path"] = $"{PassThroughFixture.ReleaseFolder}/{PassThroughFixture.FileName}",
                ["media_scope"] = "movie",
                ["pass_through_unchanged"] = true,
            });
        Require(enqueue.Status == 200, $"could not enqueue pass-through fixture: {enqueue.Payload?.ToJsonString()}");
        var jobId = enqueue.Payload?["job_id"]?.GetValue<int>() ?? 0;
        Require(jobId > 0, "pass-through enqueue did not return a job id");

        var (terminalStatus, lastError) = await WaitForPassThroughAsync(fixture, jobId);

        Require(
            File.Exists(fixture.Delivered),
            $"pass-through output was not created (status={terminalStatus}, error={lastError})");
        Require(!File.Exists(fixture.Source), "pass-through source was not cleaned up");
        Require(
            terminalStatus == "completed",
            $"pass-through job did not complete (status={terminalStatus}, error={lastError})");
        var outputSize = new FileInfo(fixture.Delivered).Length;
        var outputHash = PassThroughFixture.Sha256(fixture.Delivered);
        Require(outputSize == sourceSize, "pass-through output size changed");
        Require(outputHash == sourceHash, "pass-through output bytes changed");

        var proof = new Dictionary<string, object>
        {
            ["job_id"] = jobId,
            ["job_status"] = terminalStatus,
            ["source_removed"] = true,
            ["output_created"] = true,
            ["source_bytes"] = sourceSize,
            ["output_bytes"] = outputSize,
            ["source_sha256"] = sourceHash,
            ["output_sha256"] = outputHash,
            ["result"] = "passed",
        };
        Directory.CreateDirectory(config.ArtifactDir);
        File.WriteAllText(
            Path.Combine(config.ArtifactDir, "pass-through-proof.json"),
            JsonSerializer.Serialize(proof, AuditReport.Indented));
        Record("Processing pass-through placed byte-identical output before cleaning the watched source");
    }

    /// <summary>Waits up to 90 seconds for the job to fail, be cancelled, or complete with the output in place and the source gone.</summary>
    private async Task<(string Status, string LastError)> WaitForPassThroughAsync(PassThroughFixture fixture, int jobId)
    {
        var terminalStatus = "";
        var lastError = "";
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(90))
        {
            var inspection = await BrowserApiAsync("GET", "/api/v1/processing/jobs/inspection?limit=100");
            Require(inspection.Status == 200, "could not inspect the pass-through job");
            var row = (inspection.Payload?["jobs"] as JsonArray)?
                .OfType<JsonObject>()
                .FirstOrDefault(item => item["id"]?.GetValue<int>() == jobId);
            if (row is not null)
            {
                terminalStatus = row["status"]?.GetValue<string>() ?? "";
                lastError = row["last_error"]?.GetValue<string>() ?? "";
                if (terminalStatus is "failed" or "cancelled")
                {
                    break;
                }
            }

            if (terminalStatus == "completed" && File.Exists(fixture.Delivered) && !File.Exists(fixture.Source))
            {
                break;
            }

            await Task.Delay(250);
        }

        return (terminalStatus, lastError);
    }

    private static JsonArray ManagerConnectionIds(JsonObject library) =>
        library["manager_connection_ids"] is JsonArray ids
            ? JsonNode.Parse(ids.ToJsonString())!.AsArray()
            : [];

    private async Task SettingsHistoryAndNavigationAsync()
    {
        var cases = new[]
        {
            ("Workflows", "File paths", "processing-libraries-section", "Schedule", "/setup/workflows/schedule", "processing-schedules-section"),
            ("System", "About", "suite-settings-global", "Security", "tab=security", "suite-settings-security"),
        };
        foreach (var (sidebar, first, firstId, second, secondParam, secondId) in cases)
        {
            await OpenTabAsync(sidebar, first);
            await VisibleAsync(page.GetByTestId(firstId), $"{sidebar} history origin {first}");
            var target = page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = second, Exact = true });
            await ClickAsync(target, $"exercise {sidebar} URL history forward target");
            Require(
                page.Url.Contains(secondParam, StringComparison.Ordinal),
                $"{sidebar} {second} tab is not represented in the URL");
            await page.GoBackAsync();
            await VisibleAsync(page.GetByTestId(firstId), $"{sidebar} browser-back returns {first}");
            await page.GoForwardAsync();
            await VisibleAsync(page.GetByTestId(secondId), $"{sidebar} browser-forward returns {second}");
        }

        await page.GotoAsync(
            config.BaseUrl + "/not-a-real-screen",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await VisibleAsync(page.GetByText("This page doesn't exist."), "not-found route");
        Record("Workflows and System URL history and not-found route");
    }
}
