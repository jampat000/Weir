using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>Media manager capabilities: what each reachable manager manages, and what an unreachable one says.</summary>
[ContractArea("media_managers")]
public sealed class CapabilitiesTests(NoWebhookSecretFixture fixture) : IClassFixture<NoWebhookSecretFixture>
{
    private const string Route = $"{WeirClient.Api}/media-managers/capabilities";

    private WeirServer Server => fixture.Server;

    private async Task<WeirClient> OperatorAsync()
    {
        var admin = await Server.CreateAdminClientAsync();
        await ManagerConnections.ClearAsync(admin);
        return admin;
    }

    private static async Task<JsonObject> CreatedAsync(WeirClient client, JsonObject? overrides = null)
    {
        var response = await ManagerConnections.CreateAsync(client, overrides);
        Assert.True(response.Status == HttpStatusCode.Created, response.ToString());
        return response.Fields;
    }

    [Fact]
    public async Task Capabilities_requires_an_operator()
    {
        using var anonymous = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route)).Status);
    }

    [Fact]
    public async Task Capabilities_reports_what_a_reachable_manager_manages()
    {
        using var admin = await OperatorAsync();
        // A fake Deluno answers its manifest.
        using var fake = FakeManager.StartDeluno(libraries:
        [
            new JsonObject { ["id"] = "lib-movies", ["name"] = "Movies", ["mediaType"] = "movies", ["path"] = "/media/movies" },
            new JsonObject { ["id"] = "lib-tv", ["name"] = "TV", ["mediaType"] = "tv", ["path"] = "/media/tv" },
        ]);
        var created = await CreatedAsync(admin, new JsonObject { ["base_url"] = fake.BaseUrl, ["api_key"] = fake.ApiKey });

        var rows = (await admin.GetAsync(Route)).Elements;

        var row = Assert.Single(rows)!.AsObject();
        Assert.Equal(JsonFields.Id(created), (int)row["connection_id"]!);
        Assert.Equal($"Deluno on {new Uri(fake.BaseUrl).Host}", (string)row["label"]!);
        Assert.Equal(["movie", "tv"], row["media_scopes"]!.AsArray().Select(scope => (string)scope!));
        Assert.True((bool)row["reports_import_queue"]!);
        // The honest part: Deluno cannot clear a folder for deletion, and says so up front.
        Assert.False((bool)row["reports_library_truth"]!);
        Assert.True((bool)row["reachable"]!);
        Assert.Equal(["/media/movies", "/media/tv"], row["library_roots"]!.AsArray().Select(root => (string)root!));
        Assert.Contains("Movies and TV episodes", (string)row["summary"]!);
        var manifestRequest = Assert.Single(fake.RequestsTo("GET", "/api/integrations/external/manifest"));
        Assert.Equal(fake.ApiKey, manifestRequest.Header("X-Api-Key"));
    }

    [Fact]
    public async Task Capabilities_says_when_a_manager_did_not_answer()
    {
        using var admin = await OperatorAsync();
        using var fake = FakeManager.StartDeluno();
        var baseUrl = fake.BaseUrl;
        fake.Dispose();
        await CreatedAsync(admin, new JsonObject { ["base_url"] = baseUrl });

        var row = (await admin.GetAsync(Route)).Elements[0]!.AsObject();

        Assert.False((bool)row["reachable"]!);
        Assert.StartsWith($"Weir could not reach Deluno on {new Uri(baseUrl).Host}", (string)row["detail"]!);
        // The static profile still stands, so the page can say what this manager is for.
        Assert.Equal(["movie", "tv"], row["media_scopes"]!.AsArray().Select(scope => (string)scope!));
    }

    [Fact]
    public async Task A_connection_with_no_saved_key_is_not_listed()
    {
        // Nothing can be asked of it, so claiming a capability for it would be a lie.
        using var admin = await OperatorAsync();
        await CreatedAsync(admin, new JsonObject { ["api_key"] = string.Empty });

        Assert.Empty((await admin.GetAsync(Route)).Elements);
    }

    [Fact]
    public async Task Capabilities_is_in_the_generated_openapi_schema()
    {
        using var anonymous = Server.CreateClient();

        var response = await anonymous.GetAsync("/openapi.json");

        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        var schema = response.Fields;
        var path = schema["paths"]!["/api/v1/media-managers/capabilities"]!["get"]!;
        var reference = (string)path["responses"]!["200"]!["content"]!["application/json"]!["schema"]!["items"]!["$ref"]!;
        Assert.EndsWith("MediaManagerCapabilityOut", reference);
        var properties = schema["components"]!["schemas"]!["MediaManagerCapabilityOut"]!["properties"]!.AsObject();
        Assert.Subset(properties.Select(field => field.Key).ToHashSet(), new HashSet<string>
        {
            "label", "media_scopes", "reports_import_queue", "reports_library_truth",
        });
    }
}
