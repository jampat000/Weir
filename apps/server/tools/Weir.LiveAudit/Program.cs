using System.Text;
using Microsoft.Playwright;
using Weir.LiveAudit;

// End-to-end audit for a packaged Weir server. It does not start a server: it drives a headless browser against
// the instance at WEIR_LIVE_BASE_URL, which must be a controlled packaged instance with a deliberate data and
// runtime boundary. Screenshots and a machine-readable summary are written to WEIR_LIVE_E2E_ARTIFACTS.
//
// Exit codes: 0 every step passed, 1 an audit step or the browser failed, 2 WEIR_LIVE_BASE_URL is not set.
Console.OutputEncoding = new UTF8Encoding(false);
var config = new AuditConfig();
if (config.BaseUrl.Length == 0)
{
    await Console.Error.WriteLineAsync("WEIR_LIVE_BASE_URL is required");
    return 2;
}

Console.WriteLine($"Auditing packaged Weir at {config.BaseUrl}");
try
{
    var report = await RunAsync(config);
    Console.WriteLine(
        $"PASS  packaged live audit complete: {report.Steps.Count} steps, "
        + $"{report.Screenshots.Count} screenshots, {report.ConsoleWarnings.Count} console warnings");
    return 0;
}
catch (Exception exception)
{
    await Console.Error.WriteLineAsync($"FAIL  packaged live audit: {exception.Message}");
    return 1;
}

static async Task<AuditReport> RunAsync(AuditConfig config)
{
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
        ViewportSize = new ViewportSize { Width = 1_440, Height = 1_000 },
        IgnoreHTTPSErrors = true,
    });
    var page = await context.NewPageAsync();
    page.SetDefaultTimeout(AuditConfig.TimeoutMs);
    var audit = new LiveAudit(page, context, config);
    try
    {
        return await audit.RunAsync();
    }
    catch
    {
        await CaptureFailureAsync(page, config);
        throw;
    }
}

static async Task CaptureFailureAsync(IPage page, AuditConfig config)
{
    Directory.CreateDirectory(config.ArtifactDir);
    try
    {
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(config.ArtifactDir, "failure.png"),
            FullPage = true,
        });
    }
    catch (Exception screenshotError)
    {
        await Console.Error.WriteLineAsync($"WARN  could not capture failure screenshot: {screenshotError.Message}");
    }
}
