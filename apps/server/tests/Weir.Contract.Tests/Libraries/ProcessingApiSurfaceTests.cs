using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;

namespace Weir.Contract.Tests.Libraries;

/// <summary>
/// The Processing API surface: every promised route is published, retired ones stay gone, and the maintenance,
/// hardware and metadata provider routes answer.
/// </summary>
[ContractArea("libraries")]
public sealed class ProcessingApiSurfaceTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    /// <summary>Every surface the epic promises, as (method, path), in the generated OpenAPI schema.</summary>
    private static readonly (string Method, string Path)[] RequiredSurfaces =
    [
        // Libraries: full CRUD, reorder, discovery.
        ("GET", "/api/v1/processing/libraries"),
        ("POST", "/api/v1/processing/libraries"),
        ("GET", "/api/v1/processing/libraries/{library_id}"),
        ("PUT", "/api/v1/processing/libraries/{library_id}"),
        ("DELETE", "/api/v1/processing/libraries/{library_id}"),
        ("POST", "/api/v1/processing/libraries/reorder"),
        // Whether the reject failure policy can be offered for a library's managers (#471).
        ("GET", "/api/v1/processing/reject-support"),
        // Rule sets: CRUD, carrying sorters, metadata options and original-language options.
        ("GET", "/api/v1/processing/rule-sets"),
        ("POST", "/api/v1/processing/rule-sets"),
        ("PUT", "/api/v1/processing/rule-sets/{rule_set_id}"),
        ("DELETE", "/api/v1/processing/rule-sets/{rule_set_id}"),
        // Files: list, log, remove, requeue, move to top, bulk requeue, why-held, remove-options (#785).
        ("GET", "/api/v1/processing/files"),
        ("DELETE", "/api/v1/processing/files/{file_id}"),
        ("GET", "/api/v1/processing/files/{file_id}/remove-options"),
        ("GET", "/api/v1/processing/files/{file_id}/log"),
        ("GET", "/api/v1/processing/files/{file_id}/log/download"),
        ("GET", "/api/v1/processing/files/{file_id}/why-held"),
        ("POST", "/api/v1/processing/files/{file_id}/requeue"),
        ("POST", "/api/v1/processing/files/{file_id}/move-to-top"),
        ("POST", "/api/v1/processing/files/requeue"),
        // Rejected files: how many there are and can be processed again, and processing them all again in one step.
        ("GET", "/api/v1/processing/files/rejected/summary"),
        ("POST", "/api/v1/processing/files/rejected/process-again"),
        // Kept files (#786 review of #785): the list "keep" leaves behind, and its one way back.
        ("GET", "/api/v1/processing/kept-files"),
        ("POST", "/api/v1/processing/kept-files/{id}/process-again"),
        // Runtime control.
        ("GET", "/api/v1/pause"),
        ("PUT", "/api/v1/pause"),
        ("GET", "/api/v1/processing/operator-settings"),
        ("PUT", "/api/v1/processing/operator-settings"),
        // Maintenance: trigger and read every promoted family.
        ("GET", "/api/v1/processing/maintenance"),
        ("POST", "/api/v1/processing/maintenance/run"),
        // Hardware.
        ("GET", "/api/v1/processing/hardware"),
        // Metadata provider.
        ("GET", "/api/v1/processing/metadata-provider"),
        ("PUT", "/api/v1/processing/metadata-provider"),
        ("POST", "/api/v1/processing/metadata-provider/test"),
    ];

    /// <summary>Route prefixes that must not be published.</summary>
    private static readonly string[] RetiredSurfaces =
    [
        "/api/v1/processing/jobs/candidate-gate/enqueue",
        "/api/v1/processing/jobs/supplied-payload-evaluation/enqueue",
        // Scope-shaped views that resolved "movie" and "tv" to whichever library came first (#460).
        "/api/v1/processing/path-settings",
        "/api/v1/processing/remux-rules-settings",
    ];

    private static async Task<JsonObject> DocumentedAsync(WeirClient client)
    {
        var response = await client.GetAsync("/openapi.json");
        response.ShouldBe(HttpStatusCode.OK);
        return response.Fields["paths"]?.AsObject() ?? [];
    }

    [Fact]
    public async Task Every_capability_in_the_epic_reaches_the_v1_api()
    {
        using var client = fixture.Server.CreateClient();
        var documented = await DocumentedAsync(client);

        var missing = RequiredSurfaces
            .Where(surface => !documented.ContainsKey(surface.Path)
                || !documented[surface.Path]!.AsObject().ContainsKey(surface.Method.ToLowerInvariant()))
            .ToList();
        Assert.True(missing.Count == 0, $"Capabilities missing from the v1 API and its OpenAPI schema: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task Retired_lanes_have_not_come_back()
    {
        using var client = fixture.Server.CreateClient();
        var documented = await DocumentedAsync(client);

        var resurrected = RetiredSurfaces.Where(documented.ContainsKey).ToList();
        Assert.True(resurrected.Count == 0, $"Retired endpoints are reachable again: {string.Join(", ", resurrected)}");
    }

    // --- auth and CSRF on the new endpoints ----------------------------------------------

    [Fact]
    public async Task Maintenance_state_needs_a_session()
    {
        using var client = fixture.Server.CreateClient();

        (await client.GetAsync($"{Api}/processing/maintenance")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Maintenance_state_lists_every_promoted_family()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var body = (await admin.GetAsync($"{Api}/processing/maintenance")).Fields;

        var families = body["families"]!.AsArray().Select(row => (string)row!["family"]!).ToHashSet();
        Assert.Equal(new HashSet<string> { "work_temp_stale_sweep", "unclaimed_handbacks" }, families);
    }

    [Fact]
    public async Task Triggering_maintenance_without_a_csrf_token_is_refused()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.PostAsync(
            $"{Api}/processing/maintenance/run",
            Obj(("family", "work_temp_stale_sweep"), ("media_scope", "movie")));

        response.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Triggering_maintenance_queues_a_run()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.PostWithCsrfAsync(
            $"{Api}/processing/maintenance/run",
            Obj(("family", "work_temp_stale_sweep"), ("media_scope", "movie")));

        response.ShouldBe(HttpStatusCode.OK);
        Assert.True((bool)response.Fields["queued"]!);
    }

    [Fact]
    public async Task An_unknown_family_is_refused()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.PostWithCsrfAsync(
            $"{Api}/processing/maintenance/run",
            Obj(("family", "something_invented"), ("media_scope", "movie")));

        response.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_hardware_report_needs_a_session_and_then_answers()
    {
        using var client = fixture.Server.CreateClient();
        (await client.GetAsync($"{Api}/processing/hardware")).ShouldBe(HttpStatusCode.Unauthorized);

        await client.EnsureAdminAsync();
        var body = (await client.GetAsync($"{Api}/processing/hardware")).Fields;

        Assert.True(body.ContainsKey("available_methods"));
        Assert.True(body.ContainsKey("selectable_vendors"));
        Assert.False(string.IsNullOrEmpty((string?)body["detail"]));
    }
}
