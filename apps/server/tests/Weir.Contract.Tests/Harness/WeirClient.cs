using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness;

/// <summary>
/// One browser-like session against one server: its own cookie jar, CSRF handling and sign-in. Returns raw
/// responses so a test asserts on status codes, headers and bodies exactly as a client would see them.
/// </summary>
public sealed class WeirClient : IDisposable
{
    public const string Api = "/api/v1";
    public const string AdminUsername = "alice";
    public const string AdminPassword = "test-password-strong";

    private const string CsrfTokenField = "csrf_token";
    private const string CsrfTokenHeader = "X-CSRF-Token";

    // Above the server's 30 s SQLite busy timeout, so a lock problem shows up as the server's own error
    // rather than as a client that gave up at the same instant (#586). Keep it strictly greater.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    private readonly HttpClientHandler _handler;
    private readonly HttpClient _http;

    public WeirClient(Uri baseAddress)
    {
        _handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        };
        _http = new HttpClient(_handler) { BaseAddress = baseAddress, Timeout = RequestTimeout };
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    // --- requests -------------------------------------------------------------------------------

    public Task<WeirResponse> GetAsync(string path, params (string Name, object Value)[] query) =>
        SendAsync(HttpMethod.Get, WithQuery(path, query), content: null);

    public Task<WeirResponse> PostAsync(string path, JsonNode? body = null, IReadOnlyDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Post, path, JsonBody(body), headers);

    public Task<WeirResponse> DeleteAsync(string path, IReadOnlyDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Delete, path, content: null, headers);

    /// <summary>POST with a fresh <c>csrf_token</c> in the JSON body, the web app's shape.</summary>
    public Task<WeirResponse> PostWithCsrfAsync(string path, JsonObject? body = null) =>
        SendWithCsrfBodyAsync(HttpMethod.Post, path, body);

    public Task<WeirResponse> PutWithCsrfAsync(string path, JsonObject? body = null) =>
        SendWithCsrfBodyAsync(HttpMethod.Put, path, body);

    public Task<WeirResponse> PatchWithCsrfAsync(string path, JsonObject? body = null) =>
        SendWithCsrfBodyAsync(HttpMethod.Patch, path, body);

    /// <summary>DELETE with the token in <c>X-CSRF-Token</c> (a DELETE has no body).</summary>
    public async Task<WeirResponse> DeleteWithCsrfAsync(string path) =>
        await DeleteAsync(path, new Dictionary<string, string> { [CsrfTokenHeader] = await CsrfTokenAsync() });

    /// <summary>Opens a server-sent event stream; only the headers are read before this returns.</summary>
    public async Task<SseReader> OpenStreamAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        return new SseReader(response, await response.Content.ReadAsStreamAsync());
    }

    // --- accounts -------------------------------------------------------------------------------

    public async Task<string> CsrfTokenAsync()
    {
        var response = await GetAsync($"{Api}/auth/csrf");
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return (string)response.Fields[CsrfTokenField]!;
    }

    public async Task<WeirResponse> BootstrapAsync(string username = AdminUsername, string password = AdminPassword) =>
        await PostAsync($"{Api}/auth/bootstrap", Credentials(username, password, await CsrfTokenAsync()));

    /// <summary>Signs in; fails the test unless the server answers <paramref name="expected"/>.</summary>
    public async Task<WeirResponse> LoginAsync(
        string username = AdminUsername,
        string password = AdminPassword,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await PostAsync($"{Api}/auth/login", Credentials(username, password, await CsrfTokenAsync()));
        Assert.True(response.Status == expected, response.ToString());
        return response;
    }

    /// <summary>Creates the first admin when the install has none, then signs in as it.</summary>
    public async Task EnsureAdminAsync(string username = AdminUsername, string password = AdminPassword)
    {
        var status = await GetAsync($"{Api}/auth/bootstrap/status");
        Assert.True(status.Status == HttpStatusCode.OK, status.ToString());
        if ((bool)status.Fields["bootstrap_allowed"]!)
        {
            var created = await BootstrapAsync(username, password);
            Assert.True(created.Status == HttpStatusCode.OK, created.ToString());
        }

        await LoginAsync(username, password);
    }

    // --- plumbing -------------------------------------------------------------------------------

    private static JsonObject Credentials(string username, string password, string csrfToken) => new()
    {
        ["username"] = username,
        ["password"] = password,
        [CsrfTokenField] = csrfToken,
    };

    private async Task<WeirResponse> SendWithCsrfBodyAsync(HttpMethod method, string path, JsonObject? body)
    {
        var fields = body ?? new JsonObject();
        if (!fields.ContainsKey(CsrfTokenField))
        {
            fields[CsrfTokenField] = await CsrfTokenAsync();
        }

        return await SendAsync(method, path, JsonBody(fields));
    }

    private async Task<WeirResponse> SendAsync(
        HttpMethod method, string path, HttpContent? content, IReadOnlyDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        using var response = await _http.SendAsync(request);
        return await WeirResponse.ReadAsync(response, CancellationToken.None);
    }

    private static StringContent? JsonBody(JsonNode? body) =>
        body is null ? null : new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

    private static string WithQuery(string path, (string Name, object Value)[] query)
    {
        if (query.Length == 0)
        {
            return path;
        }

        var pairs = query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Name)}={Uri.EscapeDataString(Convert.ToString(pair.Value, CultureInfo.InvariantCulture)!)}");
        return $"{path}?{string.Join('&', pairs)}";
    }
}
