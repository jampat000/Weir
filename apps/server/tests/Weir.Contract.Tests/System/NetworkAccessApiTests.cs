using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>
/// Who can reach Weir over the network (<c>/api/v1/suite/network-access</c>).
/// Only the Windows package has a tray to carry a change out, so the contract server (a bare install) reports the
/// setting as not applicable and refuses a change with the reason. A Docker run says its port mapping decides.
/// </summary>
[ContractArea("system")]
public sealed class NetworkAccessApiTests(SystemPartASeededServerFixture fixture) : IClassFixture<SystemPartASeededServerFixture>
{
    private const string NetworkAccess = $"{WeirClient.Api}/suite/network-access";

    private static JsonObject Scope(string scope) => new() { ["scope"] = scope };

    [Fact]
    public async Task The_state_needs_a_signed_in_user()
    {
        using var client = fixture.Server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(NetworkAccess)).Status);
    }

    [Fact]
    public async Task A_bare_install_reports_not_applicable_and_points_to_the_bind_option()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var body = (await admin.GetAsync(NetworkAccess)).Fields;

        Assert.Equal("not_applicable", (string?)body["state"]);
        Assert.True(body.ContainsKey("scope"));
        Assert.Null(body["scope"]);
        Assert.True(body.ContainsKey("pending_scope"));
        Assert.Null(body["pending_scope"]);
        Assert.Equal("not_checked", (string?)body["firewall"]);
        Assert.Empty(body["addresses"]!.AsArray());
        Assert.Equal(JsonValueKind.Number, body["port"]!.GetValueKind());
        Assert.True(body["port"]!.AsValue().TryGetValue<int>(out _));
        Assert.False(string.IsNullOrEmpty((string?)body["machine_name"]));
        Assert.Contains("--host", (string)body["summary"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_viewer_can_read_the_state()
    {
        using var viewer = await SystemPartAHelpers.SignedInViewerAsync(fixture.Server);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(NetworkAccess)).Status);
    }

    [Fact]
    public async Task A_bare_install_refuses_a_change_and_says_what_decides()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.PutWithCsrfAsync(NetworkAccess, Scope("network"));

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Contains("--host", (string)response.Fields["detail"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_viewer_cannot_change_it()
    {
        using var viewer = await SystemPartAHelpers.SignedInViewerAsync(fixture.Server);

        var response = await viewer.PutWithCsrfAsync(NetworkAccess, Scope("network"));

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
    }

    [Fact]
    public async Task A_scope_that_is_not_one_of_the_two_choices_is_refused()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.PutWithCsrfAsync(NetworkAccess, Scope("everyone"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
    }

    [Fact]
    public async Task A_change_without_a_csrf_token_is_refused()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.RequestAsync(HttpMethod.Put, NetworkAccess, Scope("network"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
    }

    [Fact]
    public async Task A_change_with_a_token_that_is_not_theirs_is_refused()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.RequestAsync(HttpMethod.Put, NetworkAccess, new JsonObject { ["scope"] = "network", ["csrf_token"] = "not-a-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
    }

    [Fact]
    public async Task Docker_says_its_port_mapping_decides()
    {
        await using var server = await WeirServer.StartNewAsync(new Dictionary<string, string> { ["WEIR_RUNTIME"] = "docker" });
        using var docker = await server.CreateAdminClientAsync();

        var response = await docker.PutWithCsrfAsync(NetworkAccess, Scope("network"));

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.StartsWith("Set by Docker's port mapping", (string)response.Fields["detail"]!, StringComparison.Ordinal);
        Assert.StartsWith("Set by Docker's port mapping", (string)(await docker.GetAsync(NetworkAccess)).Fields["summary"]!, StringComparison.Ordinal);
    }
}
