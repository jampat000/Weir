using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>
/// A fake media manager (Sonarr, Radarr or Deluno): a real HTTP server that records every request. Weir talks to the managers
/// over HTTP, so the contract suite gives it something real to talk to. <see cref="StartDeluno"/> and <see cref="StartArr"/>
/// answer the minimum each product answers (the paths Weir calls) and a test scripts the rest:
/// <code>
/// using var deluno = FakeManager.StartDeluno();
/// deluno.Route("POST", "/api/integrations/processors/events", new Reply(503));
/// var calls = await deluno.WaitForRequestAsync("POST", "/api/integrations/processors/events");
/// </code>
/// A route answers with a fixed body or <see cref="Reply"/>, or with a function of the request for behaviour that depends on the
/// request or changes over time. A later route wins over an earlier one for the same request; a request no route answers gets 404.
/// </summary>
public sealed partial class FakeManager : FakeHttpServer
{
    public const string DefaultApiKey = "contract-fake-manager-api-key";

    private readonly object _gate = new();
    private readonly List<RouteEntry> _routes = [];

    private FakeManager(string kind, string apiKey)
    {
        Kind = kind;
        ApiKey = apiKey;
    }

    /// <summary>deluno, sonarr or radarr.</summary>
    public string Kind { get; }

    public string ApiKey { get; }

    /// <summary>Answers <paramref name="method"/> <paramref name="path"/> (<c>{name}</c> matches one path segment) with this body and status.</summary>
    public void Route(string method, string path, JsonNode? body = null, int status = 200) =>
        Route(method, path, new Reply(status, body));

    public void Route(string method, string path, Reply reply) => Route(method, path, _ => reply);

    public void Route(string method, string path, Func<RecordedRequest, Reply> responder)
    {
        lock (_gate)
        {
            _routes.Insert(0, new RouteEntry(method.ToUpperInvariant(), Pattern(path), responder));
        }
    }

    /// <summary>The requests received so far for <paramref name="method"/> whose path starts with <paramref name="pathPrefix"/>.</summary>
    public IReadOnlyList<RecordedRequest> RequestsTo(string method, string pathPrefix) =>
        Requests.Where(request => Targets(request, method, pathPrefix)).ToList();

    /// <summary>
    /// Waits for <paramref name="count"/> requests to <paramref name="method"/> <paramref name="pathPrefix"/> that satisfy
    /// <paramref name="where"/>, and returns all those received so far. Fails with the last requests seen after <paramref name="timeout"/>.
    /// </summary>
    public Task<IReadOnlyList<RecordedRequest>> WaitForRequestAsync(
        string method,
        string pathPrefix,
        int count = 1,
        TimeSpan? timeout = null,
        Func<RecordedRequest, bool>? where = null) =>
        WaitForAsync(
            request => Targets(request, method, pathPrefix) && (where is null || where(request)),
            count,
            timeout,
            $"The {Kind} fake: {method} {pathPrefix}");

    protected override HttpAnswer Answer(RecordedRequest request)
    {
        RouteEntry? matched;
        lock (_gate)
        {
            matched = _routes.FirstOrDefault(route => route.Method == request.Method && route.Pattern.IsMatch(request.Path));
        }

        if (matched is null)
        {
            return Reply404(request).ToAnswer();
        }

        try
        {
            return matched.Responder(request).ToAnswer();
        }
        catch (Exception problem)
        {
            // A broken script answers 500, visibly, instead of hanging the server under test.
            return new Reply(500, new JsonObject { ["message"] = $"fake responder raised: {problem}" }).ToAnswer();
        }
    }

    private static Reply Reply404(RecordedRequest request) => new(
        404, new JsonObject { ["message"] = $"the fake manager has no route for {request.Method} {request.Path}" });

    private static bool Targets(RecordedRequest request, string method, string pathPrefix) =>
        string.Equals(request.Method, method, StringComparison.OrdinalIgnoreCase) && request.Path.StartsWith(pathPrefix, StringComparison.Ordinal);

    // "/api/v3/queue/{id}": each placeholder matches one path segment.
    private static Regex Pattern(string path)
    {
        var expression = new StringBuilder("^");
        var next = 0;
        foreach (Match placeholder in Placeholder().Matches(path))
        {
            expression.Append(Regex.Escape(path[next..placeholder.Index])).Append("[^/]+");
            next = placeholder.Index + placeholder.Length;
        }

        expression.Append(Regex.Escape(path[next..])).Append('$');
        return new Regex(expression.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    [GeneratedRegex(@"\{[^/]+?\}")]
    private static partial Regex Placeholder();

    private sealed record RouteEntry(string Method, Regex Pattern, Func<RecordedRequest, Reply> Responder);
}
