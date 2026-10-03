using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Auth;

/// <summary>
/// A browser-like session for the auth tests: its own cookie jar, optional headers sent with every request, and what
/// the shared client does not expose (cookies, per-request headers on any verb, HEAD and OPTIONS, the undecoded body).
/// Responses come back as <see cref="WeirResponse"/>, so assertions read like the other areas.
/// </summary>
public sealed class AuthSession : IDisposable
{
    public const string Api = WeirClient.Api;
    public const string SessionCookie = "weir_session";

    // A browser or media manager always says what it accepts.
    private const string DefaultAccept = "*/*";

    // Above the server's 30 s SQLite busy timeout, as in the shared client.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    private readonly Uri _baseAddress;
    private readonly HttpClientHandler _handler;
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    private readonly Dictionary<string, string> _defaultHeaders;

    public AuthSession(Uri baseAddress, IReadOnlyDictionary<string, string>? defaultHeaders = null)
    {
        _baseAddress = baseAddress;
        _defaultHeaders = new Dictionary<string, string>(defaultHeaders ?? new Dictionary<string, string>());
        _handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = _cookies };
        _http = new HttpClient(_handler) { BaseAddress = baseAddress, Timeout = RequestTimeout };
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    // --- cookies --------------------------------------------------------------------------------

    public string? Cookie(string name) => _cookies.GetCookies(_baseAddress)[name]?.Value;

    public void SetCookie(string name, string value) => _cookies.Add(_baseAddress, new Cookie(name, value, "/"));

    public void ClearCookies()
    {
        foreach (Cookie cookie in _cookies.GetAllCookies())
        {
            cookie.Expired = true;
        }
    }

    /// <summary>The value of a cookie named in a response's <c>Set-Cookie</c> headers, whether or not a browser would keep it.</summary>
    public static string? SetCookieValue(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var lines))
        {
            return null;
        }

        var prefix = name + "=";
        return lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..].Split(';')[0])
            .FirstOrDefault();
    }

    // --- requests -------------------------------------------------------------------------------

    public Task<WeirResponse> GetAsync(string path, IReadOnlyDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Get, path, body: null, headers);

    public Task<WeirResponse> HeadAsync(string path) => SendAsync(HttpMethod.Head, path, body: null, headers: null);

    public Task<WeirResponse> OptionsAsync(string path, IReadOnlyDictionary<string, string> headers) =>
        SendAsync(HttpMethod.Options, path, body: null, headers);

    public Task<WeirResponse> PostAsync(string path, JsonNode? body = null, IReadOnlyDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Post, path, body, headers);

    /// <summary>POST with a fresh <c>csrf_token</c> in the JSON body (the web app's shape).</summary>
    public async Task<WeirResponse> PostWithCsrfAsync(string path, JsonObject body)
    {
        if (!body.ContainsKey("csrf_token"))
        {
            body["csrf_token"] = await CsrfTokenAsync();
        }

        return await PostAsync(path, body);
    }

    /// <summary>A GET whose body is left exactly as sent (no decompression). The caller disposes the response.</summary>
    public async Task<(HttpResponseMessage Response, byte[] Body)> GetRawAsync(string path, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = Request(HttpMethod.Get, path, content: null, headers);
        var response = await _http.SendAsync(request);
        return (response, await response.Content.ReadAsByteArrayAsync());
    }

    /// <summary>A GET that sends the cookie on this request only, without relying on the cookie jar keeping it.</summary>
    public Task<WeirResponse> GetWithCookieAsync(string path, string name, string value) =>
        GetAsync(path, new Dictionary<string, string> { ["Cookie"] = $"{name}={value}" });

    /// <summary>A header exactly as the server wrote it (no parsing, so no reordering), or null when absent.</summary>
    public static string? RawHeader(HttpResponseMessage response, string name)
    {
        foreach (var source in new[] { response.Headers.NonValidated, response.Content.Headers.NonValidated })
        {
            if (source.TryGetValues(name, out var values))
            {
                return string.Join(", ", values);
            }
        }

        return null;
    }

    /// <summary>The raw response to a POST, for a test that reads the <c>Set-Cookie</c> headers itself. The caller disposes it.</summary>
    public async Task<HttpResponseMessage> PostRawAsync(string path, JsonNode body)
    {
        using var request = Request(HttpMethod.Post, path, JsonContent(body), headers: null);
        return await _http.SendAsync(request);
    }

    // --- accounts -------------------------------------------------------------------------------

    public async Task<string> CsrfTokenAsync()
    {
        var response = await GetAsync($"{Api}/auth/csrf");
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return (string)response.Fields["csrf_token"]!;
    }

    public async Task<WeirResponse> BootstrapAsync(string username = WeirClient.AdminUsername, string password = WeirClient.AdminPassword) =>
        await PostAsync($"{Api}/auth/bootstrap", await CredentialsAsync(username, password));

    /// <summary>Signs in and returns whatever the server answers; <paramref name="extra"/> adds fields to the body.</summary>
    public async Task<WeirResponse> LoginAsync(
        string username = WeirClient.AdminUsername,
        string password = WeirClient.AdminPassword,
        IReadOnlyDictionary<string, string>? headers = null,
        params (string Name, JsonNode Value)[] extra)
    {
        var body = await CredentialsAsync(username, password);
        foreach (var (name, value) in extra)
        {
            body[name] = value;
        }

        return await PostAsync($"{Api}/auth/login", body, headers);
    }

    public async Task<WeirResponse> LogoutWithBodyAsync() =>
        await PostAsync($"{Api}/auth/logout", new JsonObject { ["csrf_token"] = await CsrfTokenAsync() });

    public async Task<WeirResponse> LogoutWithHeaderAsync() =>
        await PostAsync($"{Api}/auth/logout", body: null, new Dictionary<string, string> { ["X-CSRF-Token"] = await CsrfTokenAsync() });

    /// <summary>
    /// Creates the first admin through bootstrap when the install has none, leaving this session anonymous. Bootstrap signs
    /// the new admin in, so it leaves one working session behind; its id (dashes removed, as <c>user_sessions.id</c> is
    /// stored) is returned for a test that counts sessions, or null when an admin already existed. The cookie is read from
    /// bootstrap's own <c>Set-Cookie</c> and sent explicitly, so this works when <c>Secure</c> keeps the jar from sending it.
    /// </summary>
    public async Task<string?> EnsureAdminAccountAsync()
    {
        var status = await GetAsync($"{Api}/auth/bootstrap/status");
        Assert.True(status.Status == HttpStatusCode.OK, status.ToString());
        if (!(bool)status.Fields["bootstrap_allowed"]!)
        {
            return null;
        }

        using var created = await PostRawAsync($"{Api}/auth/bootstrap", await CredentialsAsync(WeirClient.AdminUsername, WeirClient.AdminPassword));
        Assert.True(created.StatusCode == HttpStatusCode.OK, created.ToString());
        var rawCookie = SetCookieValue(created, SessionCookie);
        Assert.False(string.IsNullOrEmpty(rawCookie), "bootstrap must set a session cookie");

        var session = await GetWithCookieAsync($"{Api}/auth/session", SessionCookie, rawCookie);
        Assert.True(session.Status == HttpStatusCode.OK, session.ToString());
        var sessionId = ((string)session.Fields["session_id"]!).Replace("-", string.Empty, StringComparison.Ordinal);
        ClearCookies();
        return sessionId;
    }

    // --- plumbing -------------------------------------------------------------------------------

    private async Task<JsonObject> CredentialsAsync(string username, string password) => new()
    {
        ["username"] = username,
        ["password"] = password,
        ["csrf_token"] = await CsrfTokenAsync(),
    };

    private async Task<WeirResponse> SendAsync(HttpMethod method, string path, JsonNode? body, IReadOnlyDictionary<string, string>? headers)
    {
        using var request = Request(method, path, body is null ? null : JsonContent(body), headers);
        using var response = await _http.SendAsync(request);
        return await WeirResponse.ReadAsync(response, CancellationToken.None);
    }

    private HttpRequestMessage Request(HttpMethod method, string path, HttpContent? content, IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("Accept", DefaultAccept);
        foreach (var (name, value) in _defaultHeaders.Concat(headers ?? new Dictionary<string, string>()))
        {
            request.Headers.Remove(name);
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private static StringContent JsonContent(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
}
