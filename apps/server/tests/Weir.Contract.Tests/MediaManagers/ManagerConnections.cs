using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>Steps on media manager connections, all through the public API.</summary>
internal static class ManagerConnections
{
    public const string Route = $"{WeirClient.Api}/media-managers/connections";

    /// <summary>
    /// POSTs a connection with the body Deluno itself sends when it wires up its connection to Weir. The name is sent and
    /// ignored: a connection is named after its kind and the host in its address.
    /// </summary>
    public static Task<WeirResponse> CreateAsync(WeirClient client, JsonObject? overrides = null) =>
        client.PostWithCsrfAsync(Route, JsonFields.Merge(
            new JsonObject
            {
                ["kind"] = "deluno",
                ["name"] = "Deluno",
                ["base_url"] = "http://192.0.2.10:5099",
                ["api_key"] = "deluno_secret_key",
                ["enabled"] = true,
            },
            overrides));

    /// <summary>The connection DELETE takes its CSRF token in a JSON body, like the web app sends it.</summary>
    public static Task<WeirResponse> DeleteAsync(WeirClient client, int connectionId) =>
        client.DeleteWithCsrfBodyAsync($"{Route}/{connectionId}");

    public static async Task ClearAsync(WeirClient client)
    {
        var listed = await client.GetAsync(Route);
        Assert.True(listed.Status == HttpStatusCode.OK, listed.ToString());
        foreach (var row in listed.Elements)
        {
            var deleted = await DeleteAsync(client, JsonFields.Id(row!));
            Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        }
    }

    /// <summary>
    /// Creates a connection for each (kind, name, address) that has none yet, so an unsigned webhook for that kind has something on
    /// file it can be attributed to: a manager kind with no connection at all is refused outright.
    /// </summary>
    public static async Task EnsureAsync(WeirClient client, IEnumerable<(string Kind, string Name, string BaseUrl)> kinds)
    {
        var listed = await client.GetAsync(Route);
        Assert.True(listed.Status == HttpStatusCode.OK, listed.ToString());
        var existing = listed.Elements.Select(row => (string)row!["kind"]!).ToHashSet();
        foreach (var (kind, name, baseUrl) in kinds.Where(wanted => !existing.Contains(wanted.Kind)))
        {
            var created = await CreateAsync(client, new JsonObject
            {
                ["kind"] = kind,
                ["name"] = name,
                ["base_url"] = baseUrl,
                ["api_key"] = "key",
            });
            Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        }
    }

    public static async Task<JsonObject> GenerateSecretAsync(WeirClient client, int connectionId)
    {
        var generated = await client.PostWithCsrfAsync($"{Route}/{connectionId}/webhook-secret");
        Assert.True(generated.Status == HttpStatusCode.OK, generated.ToString());
        return generated.Fields;
    }

    /// <summary>
    /// Creates a connection and returns it with the <c>X-Webhook-Secret</c> header for its own secret, so a webhook test proves
    /// itself as that connection rather than relying on being its kind's only one.
    /// </summary>
    public static async Task<(JsonObject Row, Dictionary<string, string> Headers)> CreateWithSecretAsync(
        WeirClient client, JsonObject? overrides = null)
    {
        var created = await CreateAsync(client, overrides);
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        var secret = await GenerateSecretAsync(client, JsonFields.Id(created.Fields));
        return (created.Fields, new Dictionary<string, string> { ["X-Webhook-Secret"] = (string)secret["webhook_secret"]! });
    }

    /// <summary>An http address on this machine where nothing is listening.</summary>
    public static string ClosedPortUrl()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
        }
        finally
        {
            probe.Stop();
        }
    }
}
