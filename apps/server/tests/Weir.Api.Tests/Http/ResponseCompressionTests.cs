using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Weir.Api.Tests.Platform;

namespace Weir.Api.Tests.Http;

/// <summary>JSON and CSV responses are compressed for a client that accepts it; the auth routes never are (#712).</summary>
public sealed class ResponseCompressionTests
{
    private static readonly Dictionary<string, string> AcceptsBrotliAndGzip = new(StringComparer.Ordinal) { ["Accept-Encoding"] = "br, gzip" };
    private static readonly Dictionary<string, string> AcceptsGzip = new(StringComparer.Ordinal) { ["Accept-Encoding"] = "gzip" };

    [Fact]
    public async Task A_json_response_is_sent_with_brotli_when_the_client_accepts_it()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var response = await client.GetAsync("/api/v1/activity/recent", AcceptsBrotliAndGzip);

        Assert.Equal(["br"], response.Content.Headers.ContentEncoding);
        await using var body = new BrotliStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        Assert.NotNull(JsonNode.Parse(body)!["items"]);
    }

    [Fact]
    public async Task A_csv_export_is_sent_with_gzip_when_that_is_what_the_client_accepts()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var response = await client.GetAsync("/api/v1/activity/export?format=csv", AcceptsGzip);

        Assert.Equal(["gzip"], response.Content.Headers.ContentEncoding);
        await using var body = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var reader = new StreamReader(body, Encoding.UTF8);
        Assert.StartsWith("id,created_at,", await reader.ReadToEndAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_csrf_token_is_never_compressed()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var response = await client.GetAsync("/api/v1/auth/csrf", AcceptsBrotliAndGzip);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.NotNull((await ApiTestClient.Json(response))["csrf_token"]);
    }

    [Fact]
    public async Task The_auth_routes_are_left_uncompressed_whatever_the_letter_case()
    {
        await using var server = await ApiTestClient.StartServerAsync();

        using var response = await new ApiTestClient(server).GetAsync("/API/V1/Auth/bootstrap/status", AcceptsBrotliAndGzip);

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task A_head_request_that_accepts_compression_answers_like_get_with_no_body()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        var client = new ApiTestClient(server);

        using var get = await client.GetAsync("/health", AcceptsGzip);
        using var head = await client.SendAsync(HttpMethod.Head, "/health", headers: AcceptsGzip);

        Assert.Equal(get.StatusCode, head.StatusCode);
        Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_client_that_asks_for_no_compression_gets_none()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var response = await client.GetAsync("/api/v1/activity/recent");

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
