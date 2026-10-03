using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Libraries;

/// <summary>The calls the library discovery tests make against the server and the library lists a fake Deluno reports.</summary>
internal static class LibrariesPartBDiscovery
{
    public const string LibrariesPath = $"{WeirClient.Api}/processing/libraries";

    private const string Connections = $"{WeirClient.Api}/media-managers/connections";

    /// <summary>One library as Deluno's manifest lists it.</summary>
    public static JsonObject ManagerLibrary(
        string id, string name, string mediaType, string rootPath, string? importWorkflow = null, string? processorOutputPath = null)
    {
        var library = new JsonObject { ["id"] = id, ["name"] = name, ["mediaType"] = mediaType, ["rootPath"] = rootPath };
        if (importWorkflow is not null)
        {
            library["importWorkflow"] = importWorkflow;
            library["processorOutputPath"] = processorOutputPath;
        }

        return library;
    }

    /// <summary>
    /// Leaves only the seeded libraries and no media manager connections, so each test starts the same way. A library a test
    /// imported is always enabled with a real watched folder, so the server's own periodic scan can queue a scan job for it
    /// before this runs, and Processing refuses to delete a library with a job queued or running. Deleting it is still
    /// attempted but best-effort: every manifest name used by a test is unique to it, so a library this could not remove never
    /// contaminates a later test's assertions.
    /// </summary>
    public static async Task ResetAsync(WeirClient client)
    {
        var libraries = await client.GetAsync(LibrariesPath);
        foreach (var row in libraries.Elements)
        {
            if ((string)row!["name"]! is not ("Movies" or "TV"))
            {
                await client.DeleteWithCsrfBodyAsync($"{LibrariesPath}/{(long)row["id"]!}");
            }
        }

        var connections = await client.GetAsync(Connections);
        foreach (var row in connections.Elements)
        {
            var deleted = await client.DeleteWithCsrfBodyAsync($"{Connections}/{(long)row!["id"]!}");
            LibrariesPartBChecks.Status(deleted, HttpStatusCode.NoContent);
        }
    }

    public static Task<WeirResponse> CreateConnectionAsync(WeirClient client, string baseUrl, string apiKey) =>
        client.PostWithCsrfAsync(Connections, new JsonObject
        {
            ["kind"] = "deluno",
            ["name"] = "Deluno",
            ["base_url"] = baseUrl,
            ["api_key"] = apiKey,
            ["enabled"] = true,
        });

    /// <summary>Connects the server to <paramref name="fake"/> and returns the new connection's id.</summary>
    public static async Task<long> ConnectAsync(WeirClient client, FakeManager fake)
    {
        var created = await CreateConnectionAsync(client, fake.BaseUrl, fake.ApiKey);
        LibrariesPartBChecks.Status(created, HttpStatusCode.Created);
        return (long)created.Fields["id"]!;
    }

    public static Task<WeirResponse> DiscoverAsync(WeirClient client, long connectionId) =>
        client.GetAsync($"{LibrariesPath}/discover/{connectionId}");

    public static Task<WeirResponse> DriftAsync(WeirClient client, long connectionId) =>
        client.GetAsync($"{LibrariesPath}/discover/{connectionId}/drift");

    public static Task<WeirResponse> ImportAsync(WeirClient client, long connectionId, params string[] keys) =>
        client.PostWithCsrfAsync(
            $"{LibrariesPath}/discover/{connectionId}/import",
            new JsonObject { ["keys"] = new JsonArray([.. keys.Select(key => (JsonNode)JsonValue.Create(key)!)]) });

    /// <summary>Imports one library and returns the library it created.</summary>
    public static async Task<JsonObject> ImportOneAsync(WeirClient client, long connectionId, string key)
    {
        var imported = await ImportAsync(client, connectionId, key);
        LibrariesPartBChecks.Status(imported, HttpStatusCode.Created);
        return imported.Elements[0]!.AsObject();
    }

    public static async Task<JsonObject> LibraryAsync(WeirClient client, long libraryId)
    {
        var response = await client.GetAsync($"{LibrariesPath}/{libraryId}");
        LibrariesPartBChecks.Status(response, HttpStatusCode.OK);
        return response.Fields;
    }
}
