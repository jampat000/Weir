using Weir.Core.MediaManagers;
using Weir.Infrastructure.ConnectionTraffic;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.ConnectionTraffic;

/// <summary>A media manager calling Weir lights its own connection, and only when Weir can tell which connection called.</summary>
public sealed class InboundConnectionActivityTests
{
    private static async Task<List<ConnectionActivity>> HeardAsync(ConnectionActivitySubscription stream)
    {
        stream.Dispose();
        var heard = new List<ConnectionActivity>();
        await foreach (var activity in stream.ReadAllAsync(CancellationToken.None))
        {
            heard.Add(activity);
        }

        return heard;
    }

    private static async Task<string> RotateSecretAsync(MediaManagerFixture fixture, long connectionId)
    {
        var row = (await fixture.Db(uow => fixture.ConnectionStore.GetAsync(uow, connectionId)))!;
        return await fixture.Db(uow => fixture.Connections.RotateWebhookSecretAsync(uow, row));
    }

    [Fact]
    public async Task A_webhook_with_a_connections_own_secret_is_an_inbound_answer_from_that_connection()
    {
        using var fixture = new MediaManagerFixture();
        var radarr = await fixture.AddConnectionAsync("radarr", "http://192.0.2.20:7878");
        var secret = await RotateSecretAsync(fixture, radarr);
        var stream = fixture.Activity.Subscribe();

        await fixture.Db(async uow => await fixture.Intake.AuthoriseAsync(uow, "radarr", secret));

        var heard = Assert.Single(await HeardAsync(stream));
        Assert.Equal(new ConnectionRef(ConnectionKind.MediaManager, radarr), heard.Connection);
        Assert.Equal(ConnectionPhase.Answered, heard.Phase);
        Assert.Equal(ConnectionDirection.Inbound, heard.Direction);
        Assert.Null(heard.Milliseconds);
    }

    [Fact]
    public async Task The_hand_off_routes_attribute_a_caller_by_its_secret_too()
    {
        using var fixture = new MediaManagerFixture();
        var first = await fixture.AddConnectionAsync("radarr", "http://192.0.2.20:7878");
        var second = await fixture.AddConnectionAsync("radarr", "http://192.0.2.21:7878");
        await RotateSecretAsync(fixture, first);
        var secondSecret = await RotateSecretAsync(fixture, second);
        var stream = fixture.Activity.Subscribe();

        await fixture.Db(async uow => await fixture.Intake.RequireSecretAsync(uow, secondSecret, "radarr"));

        var heard = Assert.Single(await HeardAsync(stream));
        Assert.Equal(new ConnectionRef(ConnectionKind.MediaManager, second), heard.Connection);
    }

    [Fact]
    public async Task A_caller_with_a_wrong_secret_lights_nothing()
    {
        using var fixture = new MediaManagerFixture();
        var radarr = await fixture.AddConnectionAsync("radarr", "http://192.0.2.20:7878");
        await RotateSecretAsync(fixture, radarr);
        var stream = fixture.Activity.Subscribe();

        await Assert.ThrowsAsync<IntakeRefusedException>(
            () => fixture.Db(async uow => await fixture.Intake.AuthoriseAsync(uow, "radarr", "not-the-secret")));

        Assert.Empty(await HeardAsync(stream));
    }

    [Fact]
    public async Task A_caller_proved_only_by_the_shared_secret_cannot_be_matched_so_lights_nothing()
    {
        using var fixture = new MediaManagerFixture(("WEIR_MEDIA_MANAGER_WEBHOOK_SECRET", "shared-secret"));
        await fixture.AddConnectionAsync("radarr", "http://192.0.2.20:7878");
        await fixture.AddConnectionAsync("radarr", "http://192.0.2.21:7878");
        var stream = fixture.Activity.Subscribe();

        await fixture.Db(async uow => await fixture.Intake.AuthoriseAsync(uow, "radarr", "shared-secret"));

        Assert.Empty(await HeardAsync(stream));
    }

    [Fact]
    public async Task The_only_connection_of_a_kind_is_the_one_that_called_even_when_it_has_no_secret()
    {
        using var fixture = new MediaManagerFixture();
        var radarr = await fixture.AddConnectionAsync("radarr", "http://192.0.2.20:7878");
        var stream = fixture.Activity.Subscribe();

        await fixture.Db(async uow => await fixture.Intake.AuthoriseAsync(uow, "radarr", presented: null));

        Assert.Equal(new ConnectionRef(ConnectionKind.MediaManager, radarr), Assert.Single(await HeardAsync(stream)).Connection);
    }
}
