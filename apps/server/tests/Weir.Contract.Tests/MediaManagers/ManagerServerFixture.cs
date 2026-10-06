using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// One server and one fresh data folder for a whole test class, plus a folder on disk for the libraries that class watches.
/// The server is stopped before the folder is deleted.
/// </summary>
public abstract class ManagerServerFixture(IReadOnlyDictionary<string, string> environment) : IAsyncLifetime
{
    private WeirServer? _server;
    private string? _folder;

    public WeirServer Server => _server ?? throw new InvalidOperationException("The server has not started yet.");

    internal LibraryFolders Library(string name) => LibraryFolders.Make(Path.Combine(Folder, name));

    private string Folder => _folder ?? throw new InvalidOperationException("The fixture has not started yet.");

    public async Task InitializeAsync()
    {
        _folder = Directory.CreateTempSubdirectory("weir_contract_files_").FullName;
        _server = await WeirServer.StartNewAsync(environment);
        await ServerStartedAsync(_server);
    }

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        if (_folder is not null)
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    protected virtual Task ServerStartedAsync(WeirServer server) => Task.CompletedTask;
}

/// <summary>A server with no instance-wide webhook secret.</summary>
public sealed class NoWebhookSecretFixture() : ManagerServerFixture(ManagerEnvironment.NoWebhookSecret);

/// <summary>
/// A server with an instance-wide webhook secret, whose Movies and TV libraries watch folders of the fixture's. Libraries are
/// pointed at them by <see cref="MoviesAsync"/>.
/// </summary>
public sealed class HandoffServerFixture() : ManagerServerFixture(Handoffs.SecretEnvironment)
{
    private LibraryFolders? _movies;
    private LibraryFolders? _tv;

    /// <summary>Gives the server's Movies and TV libraries watched folders, and returns the Movies ones.</summary>
    internal async Task<LibraryFolders> MoviesAsync()
    {
        var movies = _movies ??= Library("movies");
        var tv = _tv ??= Library("tv");
        using var admin = await Server.CreateAdminClientAsync();
        await ProcessingLibraries.EnsureAsync(admin, "Movies", "movie", movies);
        await ProcessingLibraries.EnsureAsync(admin, "TV", "tv", tv);
        return movies;
    }
}
