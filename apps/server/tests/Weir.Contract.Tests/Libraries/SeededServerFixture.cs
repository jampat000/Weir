using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Libraries;

/// <summary>
/// One server for a whole test class, with rows seeded into its database before the first test. The seed runs
/// while the server is stopped (the API cannot create these rows) and the server comes back on a new port, so a
/// test makes its clients from <see cref="Server"/> afterwards.
/// </summary>
public abstract class SeededServerFixture : IAsyncLifetime
{
    private WeirServer? _server;

    public WeirServer Server => _server ?? throw new InvalidOperationException("The server has not started yet.");

    public async Task InitializeAsync()
    {
        _server = await WeirServer.StartNewAsync();
        try
        {
            await using var database = await _server.StopForDatabaseAsync();
            Seed(database.Connection);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
    }

    protected abstract void Seed(SqliteConnection connection);
}
