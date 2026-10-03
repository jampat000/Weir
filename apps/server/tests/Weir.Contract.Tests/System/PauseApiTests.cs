using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>The pause endpoint (<c>/api/v1/pause</c>): expiry, resuming, scan-while-paused and validation.</summary>
[ContractArea("system")]
public sealed class PauseApiTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Pause = $"{WeirClient.Api}/pause";

    [Fact]
    public async Task Processing_starts_unpaused_and_says_what_a_pause_would_do()
    {
        // A fresh install: the other tests in this class pause the class's server.
        await using var fresh = await WeirServer.StartNewAsync();
        using var signedIn = await fresh.CreateAdminClientAsync();

        var body = (await signedIn.GetAsync(Pause)).Fields;

        Assert.False((bool)body["paused"]!);
        // The in-flight policy is stated up front, because the assumption otherwise is that
        // a pause stops work dead.
        Assert.Contains("already running finishes", (string)body["in_flight_policy"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pause_with_an_expiry_reports_when_it_lifts()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.PutWithCsrfAsync(Pause, new JsonObject { ["paused"] = true, ["pause_for_minutes"] = 120 });

        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        var body = response.Fields;
        Assert.True((bool)body["paused"]!);
        Assert.NotNull(body["paused_until"]);
        Assert.Contains("automatically at", (string)body["reason"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pause_with_no_expiry_says_so_rather_than_implying_one()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var body = (await admin.PutWithCsrfAsync(Pause, new JsonObject { ["paused"] = true })).Fields;

        Assert.True((bool)body["paused"]!);
        Assert.True(body.ContainsKey("paused_until"));
        Assert.Null(body["paused_until"]);
        Assert.Contains("when you resume it", (string)body["reason"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resuming_clears_the_expiry()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        await admin.PutWithCsrfAsync(Pause, new JsonObject { ["paused"] = true, ["pause_for_minutes"] = 30 });

        var body = (await admin.PutWithCsrfAsync(Pause, new JsonObject { ["paused"] = false })).Fields;

        Assert.False((bool)body["paused"]!);
        Assert.True(body.ContainsKey("paused_until"));
        Assert.Null(body["paused_until"]);
    }

    [Fact]
    public async Task Scan_while_paused_can_be_turned_off()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var body = (await admin.PutWithCsrfAsync(Pause, new JsonObject { ["paused"] = true, ["scan_while_paused"] = false })).Fields;

        Assert.False((bool)body["scan_while_paused"]!);
    }

    [Fact]
    public async Task A_pause_change_without_a_csrf_token_is_refused()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.RequestAsync(HttpMethod.Put, Pause, new JsonObject { ["paused"] = true, ["scan_while_paused"] = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
    }

    [Fact]
    public async Task An_out_of_range_duration_is_refused()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.PutWithCsrfAsync(Pause, new JsonObject { ["paused"] = true, ["pause_for_minutes"] = 0 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.Status);
    }
}
