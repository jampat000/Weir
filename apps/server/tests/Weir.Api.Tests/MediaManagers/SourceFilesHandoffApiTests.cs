using System.Net;
using Weir.Api.Tests.Platform;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.MediaManagers;

/// <summary>
/// A hand-off that lists its <c>sourceFiles</c> means those files and nothing else in the folder it names; one without a list
/// means the whole folder; one whose list names a file outside the folder, or one that is not there, is refused outright.
/// </summary>
public sealed class SourceFilesHandoffApiTests : IDisposable
{
    private const string WebhookSecret = "s3cret";
    private const string RemuxJobs = "SELECT count(*) FROM jobs WHERE job_kind = 'processing.file.remux_pass.v1'";

    private static readonly Dictionary<string, string> SecretHeader = new() { ["X-Webhook-Secret"] = WebhookSecret };

    private readonly string _watched = Path.Join(Path.GetTempPath(), "weir-source-files-" + Guid.NewGuid().ToString("N"));

    private string Release => Path.Join(_watched, "Film.2020");

    private string Film => Path.Join(Release, "film.mkv");

    private string Extra => Path.Join(Release, "extra.mkv");

    public void Dispose() => Directory.Delete(_watched, recursive: true);

    private static Task<WeirTestServer> StartAsync() =>
        WeirTestServer.StartAsync([("WEIR_SESSION_SECRET", Secret), ("WEIR_PROCESSING_WORKER_COUNT", "0"), ("WEIR_MEDIA_MANAGER_WEBHOOK_SECRET", WebhookSecret)]);

    private async Task<WeirTestServer> StartWithReleaseAsync()
    {
        Directory.CreateDirectory(Release);
        Directory.CreateDirectory(Path.Join(_watched, "Other"));
        await File.WriteAllTextAsync(Film, "the film");
        await File.WriteAllTextAsync(Extra, "an extra for seeding");
        await File.WriteAllTextAsync(Path.Join(_watched, "Other", "other.mkv"), "another film");
        var server = await StartAsync();
        await TestDatabase.ExecuteAsync(server, "UPDATE libraries SET watched_folder = $w WHERE media_type = 'movie'", ("$w", _watched));
        return server;
    }

    private Task<HttpResponseMessage> HandOffAsync(WeirTestServer server, object? sourceFiles, bool listed = true)
    {
        var body = new Dictionary<string, object?>
        {
            ["eventType"] = "deluno.processor-handoff",
            ["handoffId"] = "h1",
            ["libraryId"] = "lib-1",
            ["mediaType"] = "movies",
            ["sourcePath"] = Release,
            ["callbackPath"] = "/api/integrations/processors/events",
        };
        if (listed)
        {
            body["sourceFiles"] = sourceFiles;
        }

        return new ApiTestClient(server).PostAsync("/api/v1/intake/webhook/deluno", body, SecretHeader);
    }

    private static async Task<string[]> TargetsAsync(WeirTestServer server) =>
        (await TestDatabase.ScalarStringAsync(
            server, "SELECT group_concat(relative_path, '|') FROM (SELECT relative_path FROM media_manager_handoff_targets ORDER BY relative_path)"))?.Split('|') ?? [];

    [Fact]
    public async Task Only_the_listed_files_are_queued_recorded_and_left_where_they_are()
    {
        await using var server = await StartWithReleaseAsync();

        using var response = await HandOffAsync(server, new[] { Film });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await TestDatabase.ScalarAsync(server, RemuxJobs));
        Assert.Equal(["Film.2020/film.mkv"], await TargetsAsync(server));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM files WHERE relative_path LIKE '%extra%'"));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM jobs WHERE payload_json LIKE '%extra%'"));
        Assert.Equal("an extra for seeding", await File.ReadAllTextAsync(Extra));
    }

    [Fact]
    public async Task A_file_listed_twice_and_a_path_with_dots_in_it_count_once()
    {
        await using var server = await StartWithReleaseAsync();

        using var response = await HandOffAsync(server, new[] { Film, Path.Join(Release, "sub", "..", ".", "film.mkv") });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Film.2020/film.mkv"], await TargetsAsync(server));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("null")]
    [InlineData("empty")]
    public async Task A_hand_off_with_no_list_means_the_whole_folder(string form)
    {
        await using var server = await StartWithReleaseAsync();

        using var response = await HandOffAsync(server, form == "empty" ? Array.Empty<string>() : null, listed: form != "absent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Film.2020/extra.mkv", "Film.2020/film.mkv"], await TargetsAsync(server));
        Assert.Equal(2, await TestDatabase.ScalarAsync(server, RemuxJobs));
    }

    [Theory]
    [InlineData("outside", "'other.mkv', which is not inside the folder the hand-off names")]
    [InlineData("climbing", "'other.mkv', which is not inside the folder the hand-off names")]
    [InlineData("sibling-with-the-same-start", "'film.mkv', which is not inside the folder the hand-off names")]
    [InlineData("missing", "'gone.mkv', but Weir cannot find that file")]
    [InlineData("blank", "a file with no path")]
    public async Task A_list_naming_a_file_that_cannot_be_used_refuses_the_whole_hand_off(string which, string expected)
    {
        await using var server = await StartWithReleaseAsync();
        var bad = which switch
        {
            "outside" => Path.Join(_watched, "Other", "other.mkv"),
            "climbing" => Path.Join(Release, "..", "Other", "other.mkv"),
            "sibling-with-the-same-start" => Path.Join(_watched, "Film.2020-extras", "film.mkv"),
            "missing" => Path.Join(Release, "gone.mkv"),
            _ => string.Empty,
        };

        using var response = await HandOffAsync(server, new[] { Film, bad });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var detail = await Detail(response);
        Assert.Contains(expected, detail, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was queued.", detail, StringComparison.Ordinal);
        Assert.DoesNotContain(_watched, detail, StringComparison.Ordinal);
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, RemuxJobs));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, "SELECT count(*) FROM media_manager_handoffs"));
    }

    [Fact]
    public async Task A_list_that_is_not_a_list_of_paths_is_refused()
    {
        await using var server = await StartWithReleaseAsync();

        using var notAList = await HandOffAsync(server, Film);
        using var notText = await HandOffAsync(server, new object[] { 7 });

        Assert.All(new[] { notAList, notText }, response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
        Assert.Equal("The hand-off lists a file with no path. Nothing was queued.", await Detail(notAList));
        Assert.Equal(0, await TestDatabase.ScalarAsync(server, RemuxJobs));
    }
}
