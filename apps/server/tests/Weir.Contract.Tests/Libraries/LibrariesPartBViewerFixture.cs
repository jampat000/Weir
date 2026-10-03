using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Libraries;

/// <summary>One server for a test class, with the viewer bob already seeded before the first test runs.</summary>
public sealed class LibrariesPartBViewerFixture : IAsyncLifetime
{
    private WeirServer? _server;

    public WeirServer Server => _server ?? throw new InvalidOperationException("The server has not started yet.");

    public async Task InitializeAsync()
    {
        _server = await WeirServer.StartNewAsync();
        await LibrariesPartBViewer.EnsureAsync(_server);
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }
}
