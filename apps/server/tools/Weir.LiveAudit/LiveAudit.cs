using System.Text.Json;
using Microsoft.Playwright;

namespace Weir.LiveAudit;

/// <summary>
/// The full packaged-server audit. This file holds the small assertion and reporting helpers every step builds on;
/// the steps themselves are in the <c>LiveAudit.*.cs</c> files beside it, one per screen or area.
/// </summary>
internal sealed partial class LiveAudit
{
    private readonly IPage page;
    private readonly IBrowserContext context;
    private readonly AuditConfig config;
    private readonly Diagnostics diagnostics = new();
    private readonly List<string> steps = [];
    private readonly List<string> screens = [];
    private readonly List<string> httpChecks = [];

    public LiveAudit(IPage page, IBrowserContext context, AuditConfig config)
    {
        this.page = page;
        this.context = context;
        this.config = config;
        diagnostics.Attach(page);
    }

    public IPage Page => page;

    public async Task<AuditReport> RunAsync()
    {
        await PublicContractAsync();
        await BootstrapAndSignInAsync();
        await AuthenticatedReadSurfaceAsync();
        await ShellAndResponsiveAsync();
        await ProcessingLiveAsync();
        await LibraryAsync();
        await HistoryActivityAsync();
        await SettingsTabsAsync();
        await ActivityAndJobsAsync();
        await SystemInstanceAndSetupAsync();
        await SystemBackupsLogsSecurityAsync();
        await SettingsNotificationsAsync();
        await SettingsMediaManagersAsync();
        await ProcessingPassThroughLifecycleAsync();
        await SettingsHistoryAndNavigationAsync();
        return Finish();
    }

    private void Record(string message)
    {
        steps.Add(message);
        Console.WriteLine($"PASS  {message}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new AuditFailure(message);
        }
    }

    private async Task<ILocator> VisibleAsync(ILocator locator, string message)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = AuditConfig.TimeoutMs,
            });
        }
        catch
        {
            var body = await page.Locator("body").InnerTextAsync();
            await Console.Error.WriteLineAsync(
                $"DEBUG visible timeout: {message}; url={page.Url}; body={Truncate(body, 1_200)}; "
                + $"console_errors={Head(diagnostics.ConsoleErrors)}; page_errors={Head(diagnostics.PageErrors)}; "
                + $"failed_requests={Head(diagnostics.FailedRequests)}");
            throw;
        }

        Require(await locator.IsVisibleAsync(), message);
        return locator;
    }

    private async Task ClickAsync(ILocator locator, string message)
    {
        await (await VisibleAsync(locator, message)).ClickAsync();
        await page.WaitForTimeoutAsync(120);
    }

    /// <summary>
    /// Answers the confirmation step now sitting in front of a Remove button. Remove no longer deletes on the first
    /// click. The dialog has to name the thing it is about to delete, which is the whole point of it, so that is
    /// asserted here rather than assumed.
    /// </summary>
    private async Task ConfirmRemovalAsync(string testId, string names, string message)
    {
        var dialog = await VisibleAsync(page.GetByTestId(testId), $"{message} dialog");
        Require(
            (await dialog.InnerTextAsync()).Contains($"Remove {names}?", StringComparison.Ordinal),
            $"{message} dialog did not name {names}");
        await ClickAsync(dialog.GetByTestId($"{testId}-confirm"), message);
    }

    private async Task SettleAsync(float timeoutMs = 1_500)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            // Live polling pages intentionally stay busy. The explicit waits on the next screen are the
            // meaningful synchronization points.
        }
    }

    private async Task ScreenshotAsync(string name)
    {
        Directory.CreateDirectory(config.ArtifactDir);
        var path = Path.Combine(config.ArtifactDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        screens.Add(path);
        Record($"screenshot captured: {name}");
    }

    private async Task<IAPIResponse> GetAsync(string path, IDictionary<string, string>? headers = null)
    {
        var response = await context.APIRequest.GetAsync(
            new Uri(new Uri(config.BaseUrl + "/"), path.TrimStart('/')).AbsoluteUri,
            new APIRequestContextOptions { Headers = headers ?? new Dictionary<string, string>() });
        Require(response.Ok, $"GET {path} returned HTTP {response.Status}");
        httpChecks.Add($"GET {path} -> {response.Status}");
        return response;
    }

    /// <summary>Calls a same-origin API through the signed-in browser session.</summary>
    private async Task<ApiResult> BrowserApiAsync(string method, string path, IDictionary<string, object?>? body = null)
    {
        var element = await page.EvaluateAsync<JsonElement>(
            """
            async ({ method, path, body }) => {
              const response = await fetch(path, {
                method,
                credentials: "same-origin",
                headers: {
                  "Accept": "application/json",
                  "Content-Type": "application/json",
                  "X-Requested-With": "XMLHttpRequest",
                },
                body: body === null ? undefined : JSON.stringify(body),
              });
              const text = await response.text();
              let payload = null;
              if (text) {
                try { payload = JSON.parse(text); }
                catch { payload = { raw: text }; }
              }
              return { status: response.status, payload };
            }
            """,
            new Dictionary<string, object?> { ["method"] = method, ["path"] = path, ["body"] = body });
        var result = ApiResult.From(element);
        httpChecks.Add($"{method} {path} -> {result.Status}");
        return result;
    }

    private async Task<string> CsrfTokenAsync()
    {
        var result = await BrowserApiAsync("GET", "/api/v1/auth/csrf");
        Require(result.Status == 200, "could not obtain a CSRF token");
        var token = result.Payload?["csrf_token"]?.GetValue<string>() ?? "";
        Require(token.Length > 0, "CSRF response did not include a token");
        return token;
    }

    private static Dictionary<string, string> LowerHeaders(IAPIResponse response) =>
        response.Headers.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value);

    private static void AssertCommonHeaders(IAPIResponse response, string path)
    {
        var headers = LowerHeaders(response);
        Require(!headers.ContainsKey("server"), $"{path} exposes a Server header");
        Require(
            headers.GetValueOrDefault("cache-control", "").StartsWith("no-store", StringComparison.OrdinalIgnoreCase),
            $"{path} is not marked no-store");
    }

    private AuditReport Finish()
    {
        // Warnings are retained in the report for review. Runtime errors, failed requests and HTTP errors are
        // release blockers.
        Require(diagnostics.ConsoleErrors.Count == 0, $"browser console errors: {Head(diagnostics.ConsoleErrors, 5)}");
        Require(diagnostics.PageErrors.Count == 0, $"browser page errors: {Head(diagnostics.PageErrors, 5)}");
        Require(diagnostics.FailedRequests.Count == 0, $"browser request failures: {Head(diagnostics.FailedRequests, 5)}");
        Require(diagnostics.BadResponses.Count == 0, $"browser HTTP errors: {Head(diagnostics.BadResponses, 10)}");
        var report = new AuditReport(
            config.BaseUrl,
            config.ExpectedVersion,
            steps,
            httpChecks,
            screens,
            diagnostics.ConsoleWarnings,
            diagnostics.ConsoleErrors,
            diagnostics.PageErrors,
            diagnostics.FailedRequests,
            diagnostics.BadResponses);
        Directory.CreateDirectory(config.ArtifactDir);
        File.WriteAllText(
            Path.Combine(config.ArtifactDir, "summary.json"),
            JsonSerializer.Serialize(report, AuditReport.Indented));
        return report;
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    private static string Head(IReadOnlyList<string> items, int count = 3) =>
        "[" + string.Join(", ", items.Take(count)) + "]";
}
