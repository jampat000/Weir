using Microsoft.Playwright;

namespace Weir.LiveAudit;

/// <summary>
/// What the browser reported while the audit ran. Console warnings are kept in the report for review; console
/// errors, page errors, failed requests and HTTP errors are release blockers.
/// </summary>
internal sealed class Diagnostics
{
    private const string UnauthorizedLoad =
        "Failed to load resource: the server responded with a status of 401 (Unauthorized)";
    private const string NotFoundLoad =
        "Failed to load resource: the server responded with a status of 404 (Not Found)";

    private readonly object gate = new();
    private readonly List<string> consoleErrors = [];
    private readonly List<string> consoleWarnings = [];
    private readonly List<string> pageErrors = [];
    private readonly List<string> failedRequests = [];
    private readonly List<string> badResponses = [];
    private readonly HashSet<(string Method, string Url)> completedRequests = [];
    private readonly HashSet<string> expectedNotFoundUrls = [];
    private bool expectedNotFoundInFlight;

    public IReadOnlyList<string> ConsoleErrors => Snapshot(consoleErrors);
    public IReadOnlyList<string> ConsoleWarnings => Snapshot(consoleWarnings);
    public IReadOnlyList<string> PageErrors => Snapshot(pageErrors);
    public IReadOnlyList<string> FailedRequests => Snapshot(failedRequests);
    public IReadOnlyList<string> BadResponses => Snapshot(badResponses);

    public void Attach(IPage page)
    {
        page.Console += (_, message) => OnConsole(message);
        page.PageError += (_, error) => Add(pageErrors, error);
        page.RequestFailed += (_, request) => OnRequestFailed(request);
        page.Response += (_, response) => OnResponse(response);
    }

    /// <summary>
    /// A GET the audit expects to answer 404: Chromium reports the 404 as a console error, so it is let through
    /// for as long as the request is in flight.
    /// </summary>
    public void BeginExpectedNotFound(string url)
    {
        lock (gate)
        {
            expectedNotFoundUrls.Add(url);
            expectedNotFoundInFlight = true;
        }
    }

    public void EndExpectedNotFound()
    {
        lock (gate)
        {
            expectedNotFoundInFlight = false;
        }
    }

    private void OnConsole(IConsoleMessage message)
    {
        var text = (message.Text ?? "").Trim();
        if (text.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            // The authenticated shell probes /auth/me before it knows whether a session exists. Chromium reports
            // that expected 401 as a console error even though the response is part of the anonymous bootstrap.
            if (text == UnauthorizedLoad || (expectedNotFoundInFlight && text == NotFoundLoad))
            {
                return;
            }

            if (message.Type == "error")
            {
                consoleErrors.Add(text);
            }
            else if (message.Type == "warning")
            {
                consoleWarnings.Add(text);
            }
        }
    }

    private void OnRequestFailed(IRequest request)
    {
        var detail = request.Failure ?? "unknown failure";
        lock (gate)
        {
            if (completedRequests.Contains((request.Method, request.Url)) && detail == "net::ERR_ABORTED")
            {
                return;
            }

            // EventSource is deliberately closed when Activity unmounts or a filter/navigation replaces the
            // stream. Chromium reports that normal client-side close as ERR_ABORTED.
            if (request.Url.EndsWith("/api/v1/activity/stream", StringComparison.Ordinal)
                && detail == "net::ERR_ABORTED")
            {
                return;
            }

            failedRequests.Add($"{request.Method} {request.Url}: {detail}");
        }
    }

    private void OnResponse(IResponse response)
    {
        lock (gate)
        {
            if (response.Status < 400)
            {
                completedRequests.Add((response.Request.Method, response.Url));
                return;
            }

            if (response.Status == 401 && response.Url.TrimEnd('/').EndsWith("/api/v1/auth/me", StringComparison.Ordinal))
            {
                return;
            }

            if (response.Status == 404 && expectedNotFoundUrls.Contains(response.Url))
            {
                return;
            }

            badResponses.Add($"{response.Status} {response.Url}");
        }
    }

    private void Add(List<string> list, string text)
    {
        lock (gate)
        {
            list.Add(text);
        }
    }

    private List<string> Snapshot(List<string> list)
    {
        lock (gate)
        {
            return [.. list];
        }
    }
}
