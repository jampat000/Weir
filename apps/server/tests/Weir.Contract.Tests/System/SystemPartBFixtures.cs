using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>
/// One server for a class, seeded while it is stopped before the class's first test, then started again. Derive
/// and override <see cref="SeedAsync"/>.
/// </summary>
public abstract class SeededServerFixture : ServerFixture, IAsyncLifetime
{
    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        await using var database = await Server.StopForDatabaseAsync();
        await SeedAsync(database);
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();

    protected abstract Task SeedAsync(StoppedDatabase database);
}

/// <summary>A server with the admin <c>alice</c> and the viewer <c>bob</c> seeded after its first start.</summary>
public class UsersFixture : SeededServerFixture
{
    protected override Task SeedAsync(StoppedDatabase database)
    {
        SystemPartBHelpers.SeedUsers(database.Connection);
        return Task.CompletedTask;
    }
}

/// <summary>Users seeded, and no route to the internet (the update status route asks GitHub for the latest release).</summary>
public sealed class NoInternetUsersFixture : UsersFixture
{
    protected override IReadOnlyDictionary<string, string> Environment => SystemPartBHelpers.NoInternet;
}
