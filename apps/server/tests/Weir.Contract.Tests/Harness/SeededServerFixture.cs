using Microsoft.Data.Sqlite;

namespace Weir.Contract.Tests.Harness;

/// <summary>
/// One server for a test class, with rows seeded into its database before the class's first test. The seed runs while the
/// server is stopped (the API cannot create these rows) and the server comes back on a new port, so a test makes its
/// clients from <see cref="ServerFixture.Server"/> afterwards. Derive and override <see cref="Seed"/>; override
/// <see cref="ServerFixture.Environment"/> for settings.
/// </summary>
public abstract class SeededServerFixture : ServerFixture, IAsyncLifetime
{
    async Task IAsyncLifetime.InitializeAsync()
    {
        await InitializeAsync();
        try
        {
            await using var database = await Server.StopForDatabaseAsync();
            Seed(database.Connection);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync();

    protected abstract void Seed(SqliteConnection connection);
}
