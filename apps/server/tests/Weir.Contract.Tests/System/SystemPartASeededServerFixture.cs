using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>One server for a class, with the admin <c>alice</c> and the viewer <c>bob</c> already in its database.</summary>
public class SystemPartASeededServerFixture : IAsyncLifetime
{
    private WeirServer? _server;

    public WeirServer Server => _server ?? throw new InvalidOperationException("The server has not started yet.");

    protected virtual IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>();

    public async Task InitializeAsync()
    {
        _server = await WeirServer.StartNewAsync(Environment);
        await SystemPartAHelpers.SeedUsersAsync(_server);
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}
