namespace Weir.Contract.Tests.Harness;

/// <summary>
/// One server and one fresh data folder for a whole test class (<c>IClassFixture&lt;ServerFixture&gt;</c>), started before
/// the class's first test and stopped after its last. A class that needs extra settings derives from this and
/// overrides <see cref="Environment"/>. A test that needs a clean database or its own settings starts a server of
/// its own with <see cref="WeirServer.StartNewAsync"/> instead.
/// </summary>
public class ServerFixture : IAsyncLifetime
{
    private WeirServer? _server;

    public WeirServer Server => _server ?? throw new InvalidOperationException("The server has not started yet.");

    /// <summary>Settings on top of <see cref="ServerEnvironment"/>'s defaults.</summary>
    protected virtual IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>();

    public async Task InitializeAsync() => _server = await WeirServer.StartNewAsync(Environment);

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}
