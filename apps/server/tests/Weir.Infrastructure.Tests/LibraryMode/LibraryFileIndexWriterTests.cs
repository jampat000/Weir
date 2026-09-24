using System.Globalization;
using Weir.Core.LibraryMode;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.LibraryMode;

/// <summary>A scan's file index is brought up to date row by row, in short transactions, touching only what changed (#715).</summary>
public sealed class LibraryFileIndexWriterTests : IDisposable
{
    private const string HevcProbe = """{"streams": [{"codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160}]}""";
    private const string H264Probe = """{"streams": [{"codec_type": "video", "codec_name": "h264", "width": 1920, "height": 1080}]}""";

    private readonly StoreFixture _store = new();
    private long _libraryId;

    public void Dispose() => _store.Dispose();

    private async Task<long> LibraryAsync()
    {
        _libraryId = Convert.ToInt64(
            await _store.WithUnitOfWork(uow => uow.ExecuteScalarWriteAsync(
                "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
                "VALUES ('Index writer tests', 'movie', '/in', '/out', '/work', 1) RETURNING id")),
            CultureInfo.InvariantCulture);
        return _libraryId;
    }

    private static LibraryScanFileEntry File(string path, string? probeJson, long sizeBytes = 1_000, LibraryProblemKind? problem = null) =>
        new(path, sizeBytes, 1_700_000_000, LibraryFileClassification.Matches, null, null, 0, 0, null, null, probeJson, ProblemKind: problem);

    private Task ReplaceAsync(params LibraryScanFileEntry[] files) =>
        LibraryFileIndexWriter.ReplaceAsync(_store.Database, _libraryId, files, CancellationToken.None);

    private Task<List<(string Path, long Id, string ScannedAt)>> RowsAsync() =>
        _store.WithUnitOfWork(
            uow => uow.QueryAsync(
                "SELECT path, id, scanned_at FROM library_files WHERE library_id = @id ORDER BY path",
                reader => (reader.GetString(0), reader.GetInt64(1), reader.GetString(2)),
                ("@id", _libraryId)),
            commit: false);

    private Task<List<string>> FacetValuesAsync(string path, string facet) =>
        _store.WithUnitOfWork(
            uow => uow.QueryAsync(
                "SELECT x.value FROM library_file_facets AS x JOIN library_files AS f ON f.id = x.library_file_id " +
                "WHERE f.library_id = @id AND f.path = @path AND x.facet = @facet ORDER BY x.value",
                reader => reader.GetString(0),
                ("@id", _libraryId),
                ("@path", path),
                ("@facet", facet)),
            commit: false);

    private Task<IReadOnlyList<LibraryScanFileEntry>> CurrentAsync() =>
        _store.WithUnitOfWork(uow => LibraryScanStore.CurrentFilesAsync(uow, _libraryId), commit: false);

    [Fact]
    public async Task An_unchanged_file_keeps_its_row_untouched()
    {
        await LibraryAsync();
        await ReplaceAsync(File("/lib/a.mkv", HevcProbe));
        await _store.WithUnitOfWork(uow => uow.ExecuteAsync("UPDATE library_files SET scanned_at = '2000-01-01 00:00:00'"));
        var before = await RowsAsync();

        await ReplaceAsync(File("/lib/a.mkv", HevcProbe));

        Assert.Equal(before, await RowsAsync());
    }

    [Fact]
    public async Task A_changed_file_gets_its_new_values_probe_and_facets()
    {
        await LibraryAsync();
        await ReplaceAsync(File("/lib/a.mkv", HevcProbe));

        await ReplaceAsync(File("/lib/a.mkv", H264Probe, sizeBytes: 2_000));

        var file = Assert.Single(await CurrentAsync());
        Assert.Equal(2_000, file.SizeBytes);
        Assert.Equal(H264Probe, file.ProbeJson);
        Assert.Equal(["h264"], await FacetValuesAsync("/lib/a.mkv", LibraryFacets.VideoCodec));
    }

    [Fact]
    public async Task A_file_the_scan_no_longer_found_leaves_with_its_probe_and_facets()
    {
        await LibraryAsync();
        await ReplaceAsync(File("/lib/a.mkv", HevcProbe), File("/lib/b.mkv", H264Probe));

        await ReplaceAsync(File("/lib/b.mkv", H264Probe));

        Assert.Equal(["/lib/b.mkv"], (await CurrentAsync()).Select(file => file.Path));
        Assert.Equal(1L, await _store.WithUnitOfWork(uow => uow.CountAsync("SELECT count(*) FROM library_file_probes"), commit: false));
        Assert.Empty(await FacetValuesAsync("/lib/a.mkv", LibraryFacets.VideoCodec));
    }

    [Fact]
    public async Task A_problem_noted_since_the_last_scan_gives_way_to_the_scans_own_verdict()
    {
        await LibraryAsync();
        await ReplaceAsync(File("/lib/a.mkv", HevcProbe));
        await _store.WithUnitOfWork(async uow =>
        {
            await LibraryViewStore.RecordPreflightProblemAsync(uow, _libraryId, "/lib/a.mkv", LibraryProblemKind.Seeding);
            return 0;
        });

        await ReplaceAsync(File("/lib/a.mkv", HevcProbe));

        Assert.Null(Assert.Single(await CurrentAsync()).ProblemKind);
    }

    [Fact]
    public async Task A_problem_set_between_the_walk_and_the_write_survives_the_scan()
    {
        // The scan reads every row once up front, then writes chunks over time (#715); a preflight that records a
        // problem in that window (a user clicking Clean mid-scan) must not be reset by a chunk still holding the
        // row as the walk first saw it.
        await LibraryAsync();
        await ReplaceAsync(File("/lib/a.mkv", HevcProbe));
        var walked = await LibraryFileIndexWriter.ExistingRowsAsync(_store.Database, _libraryId, CancellationToken.None);

        await _store.WithUnitOfWork(async uow =>
        {
            await LibraryViewStore.RecordPreflightProblemAsync(uow, _libraryId, "/lib/a.mkv", LibraryProblemKind.Seeding);
            return 0;
        });

        await LibraryFileIndexWriter.WriteChunkAsync(_store.Database, _libraryId, [File("/lib/a.mkv", HevcProbe)], walked, CancellationToken.None);

        Assert.Equal(LibraryProblemKind.Seeding, Assert.Single(await CurrentAsync()).ProblemKind);
    }

    [Fact]
    public async Task A_file_without_a_probe_document_has_none_stored_and_reads_as_unknown()
    {
        await LibraryAsync();
        await ReplaceAsync(File("/lib/a.mkv", HevcProbe));

        await ReplaceAsync(File("/lib/a.mkv", null));

        Assert.Null(Assert.Single(await CurrentAsync()).ProbeJson);
        Assert.Equal([LibraryFileFacts.Unknown], await FacetValuesAsync("/lib/a.mkv", LibraryFacets.VideoCodec));
    }

    [Fact]
    public async Task A_library_larger_than_one_transaction_is_written_in_full()
    {
        await LibraryAsync();
        var files = Enumerable.Range(0, (LibraryFileIndexWriter.ChunkSize * 2) + 1)
            .Select(index => File($"/lib/{index:D5}.mkv", HevcProbe))
            .ToArray();

        await ReplaceAsync(files);

        Assert.Equal(files.Length, (await CurrentAsync()).Count);
    }
}
