using System.Globalization;
using System.Net;
using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Tests.MediaManagers;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// <see cref="HistoryFileRemovalService"/> against a real database and real temporary folders, with each manager
/// behind <see cref="FakeManagerHttp"/> (#785). Covers every route "delete" and "keep" can take — Weir alone,
/// Sonarr/Radarr's queue, and a Deluno-style hand-off — and the file-lifecycle containment the "Weir alone" route
/// depends on.
/// </summary>
public sealed class HistoryFileRemovalServiceTests : IDisposable
{
    private const string EventsPath = "/api/integrations/processors/events";
    private const string ManifestPath = "/api/integrations/external/manifest";

    private readonly MediaManagerFixture _fixture = new();
    private readonly PassFolders _folders = new();
    private readonly FileSkipMarkerStore _skipMarkers = new();

    public void Dispose()
    {
        _folders.Dispose();
        _fixture.Dispose();
    }

    private HistoryFileRemovalService Service() =>
        new(_fixture.Ports, _fixture.Connections, _fixture.Reporter, new RejectRoutes(_fixture.Ports, _fixture.Reporter), _fixture.Libraries, _fixture.Files, _skipMarkers);

    private async Task<long> LibraryAsync(string rejectedFileAction = "leave")
    {
        await _fixture.Store.Execute("DELETE FROM libraries");
        return Convert.ToInt64(await _fixture.Db(uow => uow.ExecuteScalarWriteAsync(
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, rejected_file_action, display_order) " +
            "VALUES ('Movies', 'movie', $w, $o, $k, $action, 1) RETURNING id",
            ("$w", _folders.Watched),
            ("$o", _folders.Output),
            ("$k", _folders.Work),
            ("$action", rejectedFileAction))), CultureInfo.InvariantCulture);
    }

    private Task LinkAsync(long libraryId, long connectionId) =>
        _fixture.Store.Execute($"INSERT INTO library_manager_links (library_id, connection_id) VALUES ({libraryId}, {connectionId})");

    private async Task<ProcessingFileRecord> FileRowAsync(long libraryId, string relativePath, string status = "processing_failed")
    {
        await _fixture.Store.Execute(
            $"INSERT INTO files (library_id, relative_path, status, status_reason) VALUES ({libraryId}, '{relativePath}', '{status}', 'ffmpeg could not read the audio track.')");
        return await _fixture.Db(uow => _fixture.Files.FindAsync(uow, libraryId, relativePath)) ?? throw new InvalidOperationException("Seeded row not found.");
    }

    /// <summary>A live hand-off origin for this file: a remux job carrying it, and a ledger row not yet answered.</summary>
    private async Task GiveHandoffOriginAsync(long libraryId, string relativePath, string handoffId)
    {
        await _fixture.Db(async uow => { await _fixture.Ledger.RecordReceivedAsync(uow, "deluno", handoffId, libraryId, relativePath); return 0; });
        var payload = $$$"""{"relative_media_path":"{{{relativePath}}}","library_id":{{{libraryId}}},"origin":{"source_key":"deluno","handoff_id":"{{{handoffId}}}","callback_path":"{{{EventsPath}}}"}}""";
        await _fixture.Jobs.EnqueueOrGetAsync($"seed:{handoffId}", "processing.file.remux_pass.v1", payload);
    }

    private async Task<string> ScalarText(string sql)
    {
        using var connection = _fixture.Store.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    // --- who qualifies for the choice ------------------------------------------------------------------

    [Fact]
    public async Task A_finished_title_does_not_qualify_for_a_choice()
    {
        var library = await LibraryAsync();
        _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv", "processed");

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));

        Assert.False(options.RequiresChoice);
    }

    [Fact]
    public async Task A_failed_title_whose_file_is_gone_does_not_qualify_for_a_choice()
    {
        var library = await LibraryAsync();
        // Deliberately never written to disk.
        var file = await FileRowAsync(library, "Film/film.mkv");

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));

        Assert.False(options.RequiresChoice);
    }

    // --- Weir alone --------------------------------------------------------------------------------------

    [Fact]
    public async Task With_no_linked_manager_delete_removes_the_file_itself()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));
        Assert.True(options.RequiresChoice);
        Assert.False(options.DeleteHandledByManager);
        Assert.Null(options.ManagerLabel);

        var outcome = await _fixture.Db(uow => Service().DeleteAsync(uow, file, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.False(File.Exists(source));
        Assert.Equal(0, await _fixture.Store.Scalar("SELECT count(*) FROM files"));
        Assert.Equal(1, await _fixture.Store.Scalar($"SELECT count(*) FROM activity_events WHERE event_type = '{Weir.Core.Activity.ActivityEventTypes.ProcessingFileRemovalDeleted}'"));
    }

    [Fact]
    public async Task With_no_linked_manager_keep_records_a_skip_marker_and_forgets_the_row()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));
        Assert.False(options.KeepNotifiesManager);

        var outcome = await _fixture.Db(uow => Service().KeepAsync(uow, file, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.True(File.Exists(source));
        Assert.Equal(0, await _fixture.Store.Scalar("SELECT count(*) FROM files"));
        Assert.Equal(1, await _fixture.Store.Scalar("SELECT count(*) FROM file_skip_markers"));
    }

    [Fact]
    public async Task Delete_refuses_a_relative_path_that_escapes_the_watched_folder()
    {
        var library = await LibraryAsync();
        var file = await FileRowAsync(library, "../outside.mkv");

        var outcome = await _fixture.Db(uow => Service().DeleteAsync(uow, file, CancellationToken.None));

        Assert.False(outcome.Done);
        Assert.Equal(1, await _fixture.Store.Scalar("SELECT count(*) FROM files"));
    }

    // --- through Sonarr / Radarr's queue ------------------------------------------------------------------

    [Fact]
    public async Task With_radarr_linked_delete_removes_the_queue_item_through_radarr()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");
        var connection = await _fixture.AddConnectionAsync("radarr", "Radarr", "http://10.0.0.5:7878", "k");
        await LinkAsync(library, connection);
        var outputPath = Path.GetFullPath(source).Replace('\\', '/');
        _fixture.Http.Json(HttpMethod.Get, "/api/v3/queue", $$"""{"records":[{"id":55,"outputPath":"{{outputPath}}","downloadId":"dl1"}]}""");
        _fixture.Http.Route(HttpMethod.Delete, "/api/v3/queue/55", _ => FakeManagerHttp.Response(HttpStatusCode.OK));

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));
        Assert.True(options.DeleteHandledByManager);
        Assert.Equal("Radarr", options.ManagerLabel);

        var outcome = await _fixture.Db(uow => Service().DeleteAsync(uow, file, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.Single(_fixture.Http.RequestsTo(HttpMethod.Delete, "/api/v3/queue/55"));
        Assert.Equal(0, await _fixture.Store.Scalar("SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task A_manager_that_refuses_the_queue_removal_fails_the_whole_action()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");
        var connection = await _fixture.AddConnectionAsync("radarr", "Radarr", "http://10.0.0.5:7878", "k");
        await LinkAsync(library, connection);
        // No matching queue row: the same "cannot tell which download" refusal the automatic reject job hits.
        _fixture.Http.Json(HttpMethod.Get, "/api/v3/queue", """{"records":[]}""");

        var outcome = await _fixture.Db(uow => Service().DeleteAsync(uow, file, CancellationToken.None));

        Assert.False(outcome.Done);
        Assert.True(File.Exists(source));
        Assert.Equal(1, await _fixture.Store.Scalar("SELECT count(*) FROM files"));
        Assert.Equal("processing_failed", await ScalarText("SELECT status FROM files"));
    }

    // --- through a hand-off (Deluno) -----------------------------------------------------------------------

    [Fact]
    public async Task With_a_live_handoff_delete_reports_rejected_and_removes_the_file()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");
        var connection = await _fixture.AddConnectionAsync("deluno", "Deluno", "http://192.0.2.30:5099", "k1");
        await LinkAsync(library, connection);
        await GiveHandoffOriginAsync(library, "Film/film.mkv", "h1");
        _fixture.Http.Json(HttpMethod.Get, ManifestPath, """{"capabilities":["processor-reject-regrab"],"libraries":[]}""");
        _fixture.Http.Json(HttpMethod.Post, EventsPath, "{}", HttpStatusCode.Accepted);

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));
        Assert.True(options.DeleteHandledByManager);
        Assert.Equal("Deluno", options.ManagerLabel);

        var outcome = await _fixture.Db(uow => Service().DeleteAsync(uow, file, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.False(File.Exists(source));
        var post = Assert.Single(_fixture.Http.RequestsTo(HttpMethod.Post, EventsPath));
        var body = (Weir.Core.Json.WireObject)post.Json!;
        Assert.Equal("rejected", Weir.Core.Json.WireConvert.Str(body["disposition"]));
        Assert.Equal(0, await _fixture.Store.Scalar("SELECT count(*) FROM files"));
    }

    [Fact]
    public async Task A_handoff_manager_that_cannot_replace_the_release_falls_back_to_weir_deleting_it()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");
        var connection = await _fixture.AddConnectionAsync("deluno", "Deluno", "http://192.0.2.30:5099", "k1");
        await LinkAsync(library, connection);
        await GiveHandoffOriginAsync(library, "Film/film.mkv", "h2");
        // No reject-regrab capability advertised: the handoff target cannot take the rejection, but it is still the
        // only linked connection, so there is no queue route to fall back to either.
        _fixture.Http.Json(HttpMethod.Get, ManifestPath, """{"capabilities":[],"libraries":[]}""");

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));
        Assert.False(options.DeleteHandledByManager);

        var outcome = await _fixture.Db(uow => Service().DeleteAsync(uow, file, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.False(File.Exists(source));
        Assert.Empty(_fixture.Http.RequestsTo(HttpMethod.Post, EventsPath));
    }

    [Fact]
    public async Task With_a_live_handoff_keep_tells_the_manager_it_will_not_be_imported()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");
        var connection = await _fixture.AddConnectionAsync("deluno", "Deluno", "http://192.0.2.30:5099", "k1");
        await LinkAsync(library, connection);
        await GiveHandoffOriginAsync(library, "Film/film.mkv", "h3");
        _fixture.Http.Json(HttpMethod.Get, ManifestPath, """{"capabilities":["processor-reject-regrab"],"libraries":[]}""");
        _fixture.Http.Json(HttpMethod.Post, EventsPath, "{}", HttpStatusCode.Accepted);

        var options = await _fixture.Db(uow => Service().EvaluateAsync(uow, file, CancellationToken.None));
        Assert.True(options.KeepNotifiesManager);
        Assert.Equal("Deluno", options.ManagerLabel);

        var outcome = await _fixture.Db(uow => Service().KeepAsync(uow, file, CancellationToken.None));

        Assert.True(outcome.Done);
        Assert.True(File.Exists(source));
        var post = Assert.Single(_fixture.Http.RequestsTo(HttpMethod.Post, EventsPath));
        var body = (Weir.Core.Json.WireObject)post.Json!;
        Assert.Equal("failed", Weir.Core.Json.WireConvert.Str(body["status"]));
        // "held", not "rejected": the file is not going anywhere, so nothing is reported removed.
        Assert.Equal("held", Weir.Core.Json.WireConvert.Str(body["disposition"]));
        Assert.False(((Weir.Core.Json.WireBool)body["sourceRemoved"]).Value);
        Assert.Equal(1, await _fixture.Store.Scalar("SELECT count(*) FROM file_skip_markers"));
    }

    [Fact]
    public async Task A_handoff_manager_that_refuses_the_keep_report_fails_the_whole_action()
    {
        var library = await LibraryAsync();
        var source = _folders.Source("Film/film.mkv");
        var file = await FileRowAsync(library, "Film/film.mkv");
        var connection = await _fixture.AddConnectionAsync("deluno", "Deluno", "http://192.0.2.30:5099", "k1");
        await LinkAsync(library, connection);
        await GiveHandoffOriginAsync(library, "Film/film.mkv", "h4");
        _fixture.Http.Json(HttpMethod.Get, ManifestPath, """{"capabilities":["processor-reject-regrab"],"libraries":[]}""");
        _fixture.Http.Json(HttpMethod.Post, EventsPath, "{}", HttpStatusCode.Conflict);

        var outcome = await _fixture.Db(uow => Service().KeepAsync(uow, file, CancellationToken.None));

        Assert.False(outcome.Done);
        Assert.True(File.Exists(source));
        Assert.Equal(0, await _fixture.Store.Scalar("SELECT count(*) FROM file_skip_markers"));
        Assert.Equal(1, await _fixture.Store.Scalar("SELECT count(*) FROM files"));
    }
}
