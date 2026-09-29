using System.Net;
using Weir.Api.Tests.Platform;
using Weir.Infrastructure.Sqlite;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>Connections saved under a typed name take their derived name when Weir starts.</summary>
public sealed class ConnectionNamesStartupTests
{
    private static void SeedTypedNames(string home)
    {
        var dbPath = Path.Join(home, "data", "weir.sqlite3");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var database = new SqliteDatabase(dbPath);
        new SchemaMigrator(database).EnsureAtHead();
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO media_manager_connections (kind, name, enabled, base_url) VALUES " +
                "('radarr', 'Movies', 1, 'http://nas:7878'), ('radarr', '4K movies', 1, 'http://nas:7879'), ('deluno', 'Deluno', 1, 'http://RIG:5099');" +
                "INSERT INTO download_client_connections (kind, name, enabled, base_url) VALUES ('qbittorrent', 'Living room', 1, 'http://10.1.1.51:8080');";
            command.ExecuteNonQuery();
        }

        database.ClearPool();
    }

    [Fact]
    public async Task Existing_media_managers_and_download_clients_are_renamed_after_their_address_on_start()
    {
        await using var server = await WeirTestServer.StartAsync(
            [("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0")],
            prepareHome: SeedTypedNames);
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var managers = await client.GetAsync("/api/v1/media-managers/connections");
        using var downloadClients = await client.GetAsync("/api/v1/download-clients/connections");

        Assert.Equal(HttpStatusCode.OK, managers.StatusCode);
        Assert.Equal(
            ["Radarr on nas (7878)", "Radarr on nas (7879)", "Deluno on RIG"],
            (await Json(managers)).AsArray().Select(row => row!["name"]!.GetValue<string>()));
        Assert.Equal(["qBittorrent on 10.1.1.51"], (await Json(downloadClients)).AsArray().Select(row => row!["name"]!.GetValue<string>()));
    }
}
