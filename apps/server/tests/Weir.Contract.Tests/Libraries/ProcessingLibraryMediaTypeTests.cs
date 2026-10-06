using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;
using static Weir.Contract.Tests.Libraries.LibraryRequests;

namespace Weir.Contract.Tests.Libraries;

/// <summary>A library's media type and folders: overlap rules, several libraries of one type, and which library a hand-off lands in.</summary>
[ContractArea("libraries")]
public sealed class ProcessingLibraryMediaTypeTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    // Disabled so the server's periodic scan never queues work that would block the cleanup delete.
    private static Task<WeirResponse> Create(WeirClient client, params (string Name, JsonNode? Value)[] overrides) =>
        CreateAsync(
            client,
            Obj(
                ("enabled", false),
                ("name", "Films 4K"),
                ("media_type", "movie"),
                ("watched_folder", "/srv/films4k/in"),
                ("output_folder", "/srv/films4k/out")),
            overrides);

    private async Task<WeirClient> OperatorAsync()
    {
        var admin = await fixture.Server.CreateAdminClientAsync();
        try
        {
            await RemoveAddedLibrariesAsync(admin);
        }
        catch
        {
            admin.Dispose();
            throw;
        }

        return admin;
    }

    [Fact]
    public async Task The_scope_shaped_routes_are_gone()
    {
        using var operatorClient = await OperatorAsync();

        foreach (var path in new[] { $"{Api}/processing/path-settings", $"{Api}/processing/remux-rules-settings" })
        {
            Assert.True((await operatorClient.GetAsync(path)).Status == HttpStatusCode.NotFound, path);
        }
    }

    [Fact]
    public async Task A_library_reports_its_media_type()
    {
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient);

        response.ShouldBe(HttpStatusCode.Created);
        Assert.Equal("movie", (string)response.Fields["media_type"]!);
        Assert.False(response.Fields.ContainsKey("media_scope"));
    }

    [Theory]
    [InlineData("output_folder", "/srv/films4k/in/done", "watched folder and output folder overlap")]
    [InlineData("work_folder", "/srv/films4k/in", "watched folder and work folder overlap")]
    [InlineData("output_folder", "", "Set an output folder")]
    public async Task A_library_whose_own_folders_overlap_is_refused(string field, string value, string expected)
    {
        using var operatorClient = await OperatorAsync();

        var response = await Create(operatorClient, (field, value));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains(expected, (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task A_library_cannot_share_another_librarys_folders()
    {
        using var operatorClient = await OperatorAsync();
        (await Create(operatorClient)).ShouldBe(HttpStatusCode.Created);

        var response = await Create(
            operatorClient,
            ("name", "Films 1080p"),
            ("watched_folder", "/srv/films4k/in/1080p"),
            ("output_folder", "/srv/films1080/out"));

        response.ShouldBe(HttpStatusCode.BadRequest);
        Assert.Contains("overlaps the watched folder of 'Films 4K'", (string)response.Fields["detail"]!);
    }

    [Fact]
    public async Task A_second_film_library_is_normal()
    {
        using var operatorClient = await OperatorAsync();
        var first = await Create(operatorClient);
        var second = await Create(
            operatorClient,
            ("name", "Films 1080p"),
            ("watched_folder", "/srv/films1080/in"),
            ("output_folder", "/srv/films1080/out"));
        first.ShouldBe(HttpStatusCode.Created);
        second.ShouldBe(HttpStatusCode.Created);

        var edited = await operatorClient.PutWithCsrfAsync(
            $"{LibrariesUrl}/{(long)second.Fields["id"]!}",
            Obj(
                ("enabled", false),
                ("name", "Films 1080p"),
                ("media_type", "movie"),
                ("watched_folder", "/srv/films1080/incoming"),
                ("output_folder", "/srv/films1080/out")));

        edited.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_hand_off_lands_in_the_library_whose_folder_holds_the_file()
    {
        // Resolving by type alone always picked the first film library, whatever folder the file was in.
        // A fresh install: a Deluno connection with no webhook secret of its own, and no queued jobs.
        await using var server = await WeirServer.StartNewAsync();
        long secondId;
        using (var client = await server.CreateAdminClientAsync())
        {
            var connection = await client.PostWithCsrfAsync(
                $"{Api}/media-managers/connections",
                Obj(
                    ("kind", "deluno"),
                    ("name", "Deluno"),
                    ("base_url", "http://192.0.2.10:5099"),
                    ("api_key", "deluno_secret_key"),
                    ("enabled", true)));
            connection.ShouldBe(HttpStatusCode.Created);
            (await Create(client, ("enabled", true))).ShouldBe(HttpStatusCode.Created); // Films 4K: /srv/films4k/in
            var second = await Create(
                client,
                ("enabled", true),
                ("name", "Films 1080p"),
                ("watched_folder", "/srv/films1080/in"),
                ("output_folder", "/srv/films1080/out"));
            second.ShouldBe(HttpStatusCode.Created);
            secondId = (long)second.Fields["id"]!;
            var response = await client.PostAsync(
                $"{Api}/intake/webhook/deluno",
                Obj(
                    ("eventType", "deluno.processor-handoff"),
                    ("handoffId", "h-1080"),
                    ("mediaType", "movies"),
                    ("sourcePath", "/srv/films1080/in/Heat.1995/heat.mkv")));
            response.ShouldBe(HttpStatusCode.OK);
        }

        // Stopped first, so reading the database does not bring the server back.
        await server.StopAsync();
        await using var database = await server.StopForDatabaseAsync();
        var jobs = SeedSql.Rows(database.Connection, "SELECT payload_json FROM jobs WHERE dedupe_key LIKE $key", ("$key", "%handoff:h-1080"));
        var job = Assert.Single(jobs);
        var payload = JsonNode.Parse((string?)job["payload_json"] ?? "{}")!;
        Assert.Equal(secondId, (long)payload["library_id"]!);
        Assert.Equal("Heat.1995/heat.mkv", (string)payload["relative_media_path"]!);
        Assert.Equal("movie", (string)payload["media_scope"]!);
    }
}
