using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// A server with no instance-wide webhook secret, so every kind it exercises unsigned needs its own connection on file first
/// (a kind with no connection at all is refused outright). Sonarr, Radarr and Deluno each get one before the first test.
/// </summary>
public sealed class IntakeFixture() : ManagerServerFixture(ManagerEnvironment.NoWebhookSecret)
{
    private static readonly (string Kind, string Name, string BaseUrl)[] EveryKind =
    [
        ("sonarr", "Sonarr", "http://192.0.2.30:8989"),
        ("radarr", "Radarr", "http://192.0.2.20:7878"),
        ("deluno", "Deluno", "http://192.0.2.10:5099"),
    ];

    private LibraryFolders? _movies;
    private LibraryFolders? _tv;

    internal LibraryFolders Movies => _movies ??= Library("movies");

    internal LibraryFolders Tv => _tv ??= Library("tv");

    protected override async Task ServerStartedAsync(WeirServer server)
    {
        using var setup = await server.CreateAdminClientAsync();
        await ManagerConnections.EnsureAsync(setup, EveryKind);
    }

    /// <summary>The admin client, on a server whose Movies and TV libraries have watched folders.</summary>
    internal async Task<WeirClient> WithWatchedFoldersAsync()
    {
        var admin = await Server.CreateAdminClientAsync();
        await ProcessingLibraries.EnsureAsync(admin, "Movies", "movie", Movies);
        await ProcessingLibraries.EnsureAsync(admin, "TV", "tv", Tv);
        return admin;
    }
}
