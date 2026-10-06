using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Libraries;

/// <summary>
/// Seeding and reading the Library view's scan index, shared by the Library view contract tests.
/// The scan index is seeded straight into <c>library_files</c> (with the derived columns and facet rows a scan
/// writes) while the server is stopped, because what is under test is the reading of that index. Running a
/// real scan needs real media files and is covered by the server's own test suite.
/// </summary>
public static class LibraryViewSeed
{
    public const string LibrariesUrl = LibrariesPartAHelpers.Api + "/processing/libraries";

    public const string FilmProbe = """
        {"streams":[
          {"codec_type":"video","codec_name":"hevc","width":3840,"height":2160},
          {"codec_type":"audio","codec_name":"eac3","channels":6,"channel_layout":"5.1(side)","tags":{"language":"eng"}},
          {"codec_type":"audio","codec_name":"aac","channels":2,"tags":{"language":"jpn"}},
          {"codec_type":"subtitle","codec_name":"subrip","tags":{"language":"eng"}}]}
        """;

    public const string ShowProbe = """
        {"streams":[
          {"codec_type":"video","codec_name":"h264","width":1920,"height":1080},
          {"codec_type":"audio","codec_name":"ac3","channels":6,"tags":{"language":"eng"}}]}
        """;

    // One seeded row: the columns a scan derives from the probe JSON, and the facet rows it writes with them.
    // Kept in the test rather than computed, so a change to the server's own derivation shows up here as a
    // failure instead of being mirrored automatically.
    private static readonly Dictionary<string, ScanFacts> Facts = new()
    {
        [FilmProbe] = new(
            "hevc", 2160, "4k", 2, 1, "eng eac3 5.1, jpn aac stereo", "eng",
            [
                ("video_codec", "hevc"),
                ("resolution", "4k"),
                ("audio", "eac3 5.1"),
                ("audio", "aac stereo"),
                ("audio_language", "eng"),
                ("audio_language", "jpn"),
                ("subtitle_language", "eng"),
            ]),
        [ShowProbe] = new(
            "h264", 1080, "1080p", 1, 0, "eng ac3 5.1", null,
            [
                ("video_codec", "h264"),
                ("resolution", "1080p"),
                ("audio", "ac3 5.1"),
                ("audio_language", "eng"),
            ]),
        [string.Empty] = new(
            "unknown", null, "unknown", 0, 0, null, null,
            [("video_codec", "unknown"), ("resolution", "unknown")]),
    };

    public static void InsertLibraryFile(
        SqliteConnection connection,
        long libraryId,
        string path,
        string classification,
        string probeJson,
        long sizeBytes = 1_000_000,
        long? linkCount = null,
        string? problemKind = null,
        string? managerKind = null,
        string? managerTitle = null)
    {
        var facts = Facts[probeJson];
        var fileId = SeedSql.InsertAndGetId(
            connection,
            "INSERT INTO library_files (library_id, path, size_bytes, mtime, classification, "
                + "removed_audio_tracks, removed_subtitle_tracks, estimated_bytes_saved, video_codec, "
                + "video_height, resolution_class, audio_track_count, subtitle_track_count, audio_summary, "
                + "subtitle_summary, link_count, problem_kind, manager_kind, manager_title) "
                + "VALUES ($library, $path, $size, 1700000000, $classification, 0, 0, 0, $codec, $height, $resolution, "
                + "$audioCount, $subtitleCount, $audioSummary, $subtitleSummary, $linkCount, $problem, $managerKind, $managerTitle)",
            ("$library", libraryId), ("$path", path), ("$size", sizeBytes), ("$classification", classification),
            ("$codec", facts.VideoCodec), ("$height", facts.VideoHeight), ("$resolution", facts.ResolutionClass),
            ("$audioCount", facts.AudioTrackCount), ("$subtitleCount", facts.SubtitleTrackCount),
            ("$audioSummary", facts.AudioSummary), ("$subtitleSummary", facts.SubtitleSummary),
            ("$linkCount", linkCount), ("$problem", problemKind), ("$managerKind", managerKind), ("$managerTitle", managerTitle));
        SeedSql.Execute(
            connection,
            "INSERT INTO library_file_probes (library_file_id, probe_json) VALUES ($file, $probe)",
            ("$file", fileId), ("$probe", probeJson));
        foreach (var (facet, value) in facts.Facets)
        {
            SeedSql.Execute(
                connection,
                "INSERT OR IGNORE INTO library_file_facets (library_id, library_file_id, facet, value) VALUES ($library, $file, $facet, $value)",
                ("$library", libraryId), ("$file", fileId), ("$facet", facet), ("$value", value));
        }
    }

    /// <summary>Replaces the first library's scan index with four deliberately varied files; returns its id.</summary>
    public static long SeedScanIndex(SqliteConnection connection)
    {
        var libraryId = LibrariesPartAHelpers.FirstLibraryId(connection);
        SeedSql.Execute(connection, "DELETE FROM library_files WHERE library_id = $library", ("$library", libraryId));
        InsertLibraryFile(
            connection, libraryId, "/lib/film.mkv", "would_change", FilmProbe, sizeBytes: 3_000,
            managerKind: "radarr", managerTitle: "Blade Runner 2049");
        InsertLibraryFile(connection, libraryId, "/lib/show.mkv", "matches", ShowProbe, sizeBytes: 1_000);
        InsertLibraryFile(connection, libraryId, "/lib/seeding.mkv", "would_change", ShowProbe, sizeBytes: 2_000, linkCount: 2);
        InsertLibraryFile(connection, libraryId, "/lib/broken.mkv", "cannot_process", string.Empty, sizeBytes: 500, problemKind: "unreadable");
        return libraryId;
    }

    /// <summary>
    /// A server of its own with the scan index seeded, for a test that changes what it reads back and so must not
    /// share the class's server. The caller disposes it.
    /// </summary>
    public static async Task<(WeirServer Server, long LibraryId)> StartScannedServerAsync()
    {
        var server = await WeirServer.StartNewAsync();
        try
        {
            await using var database = await server.StopForDatabaseAsync();
            return (server, SeedScanIndex(database.Connection));
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    public static async Task<JsonObject> OverviewAsync(WeirClient client, long libraryId)
    {
        var response = await client.GetAsync($"{LibrariesUrl}/{libraryId}/library-overview");
        response.ShouldBe(HttpStatusCode.OK);
        return response.Fields;
    }

    public static async Task<JsonObject> FilesAsync(WeirClient client, long libraryId, string query = "")
    {
        var response = await client.GetAsync($"{LibrariesUrl}/{libraryId}/library-files" + (query.Length == 0 ? "" : $"?{query}"));
        response.ShouldBe(HttpStatusCode.OK);
        return response.Fields;
    }

    public static List<string> Paths(JsonObject body) =>
        body["files"]!.AsArray().Select(file => (string)file!["path"]!).ToList();

    private sealed record ScanFacts(
        string VideoCodec,
        int? VideoHeight,
        string ResolutionClass,
        int AudioTrackCount,
        int SubtitleTrackCount,
        string? AudioSummary,
        string? SubtitleSummary,
        (string Facet, string Value)[] Facets);
}
