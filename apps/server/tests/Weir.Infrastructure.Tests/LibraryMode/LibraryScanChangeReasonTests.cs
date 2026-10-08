using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Jobs;
using Weir.Core.LibraryMode;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Media;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Media;
using Weir.Infrastructure.Tests.MediaManagers;
using Weir.Infrastructure.Tests.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.LibraryMode;

/// <summary>
/// Why a file needs cleaning (new, replaced, rules changed) is worked out by the scan, which reads the index it is about to
/// replace, and kept on each file's row so the next scan can carry it forward. It is never guessed: with nothing earlier to
/// compare with, a file has no reason.
/// </summary>
public sealed class LibraryScanChangeReasonTests : IDisposable
{
    private readonly MediaManagerFixture _fixture = new();
    private readonly TempDirectory _libraryFolder = new();
    private readonly FakeMediaRunner _media = new();
    private readonly LibraryScanStore _scans = new();
    private readonly LibrarySettingsStore _librarySettings = new();

    public void Dispose()
    {
        _libraryFolder.Dispose();
        _fixture.Dispose();
    }

    private LibraryScanHandler Handler() => new(
        _fixture.Store.Database,
        new MediaTools(_media, new FixedResolver(), new ListLogger<MediaTools>(), TimeProvider.System),
        _fixture.Connections,
        PhysicalHardlinkInspector.Instance,
        _fixture.Jobs,
        new RedownloadRiskChecker(new ArrRedownloadRiskGateway(_fixture.Http)),
        _scans,
        _librarySettings,
        new LibraryFileMarksStore(),
        new LibraryViewStore(),
        new LibraryStore(),
        new LibraryScanProgress(),
        _fixture.Store.Clock,
        NullLogger<LibraryScanHandler>.Instance);

    private async Task ScanAsync(long libraryId)
    {
        const string leaseOwner = "test-worker";
        var jobId = await _fixture.Store.WithUnitOfWork(async uow => (await _scans.RequestScanAsync(uow, _fixture.Jobs, libraryId, "manual")).Id);
        var claimed = await _fixture.Jobs.ClaimNextAsync(leaseOwner, _fixture.Store.Clock.GetUtcNow().AddMinutes(5), _fixture.Store.Clock.GetUtcNow());
        Assert.NotNull(claimed);
        Assert.Equal(jobId, claimed!.Id);
        await Handler().HandleAsync(new JobWorkContext(claimed.Id, claimed.JobKind, claimed.PayloadJson, leaseOwner), CancellationToken.None);
        Assert.True(await _fixture.Jobs.CompleteClaimedAsync(claimed.Id, leaseOwner));
    }

    private async Task<Dictionary<string, string?>> ReasonsAsync(long libraryId) =>
        (await _fixture.Db(uow => uow.QueryAsync(
            "SELECT path, change_reason FROM library_files WHERE library_id = @id",
            reader => (Path: SqliteValues.GetString(reader, 0), Reason: SqliteValues.GetStringOrNull(reader, 1)),
            ("@id", libraryId)), commit: false)).ToDictionary(row => row.Path, row => row.Reason);

    /// <summary>A library that has been scanned once, holding one file the rules would change.</summary>
    private async Task<(long Library, string Old, string Fresh)> ScannedOnceAsync()
    {
        var library = Convert.ToInt64(await _fixture.Db(uow => uow.ExecuteScalarWriteAsync(
            "INSERT INTO libraries (name, media_type, watched_folder, output_folder, work_folder, display_order) " +
            "VALUES ('Movies library', 'movie', '/downloads/watched', '/downloads/output', '/downloads/work', 1) RETURNING id")), CultureInfo.InvariantCulture);
        await _fixture.Db(async uow =>
        {
            await _librarySettings.SetAsync(uow, library, new LibrarySettings([_libraryFolder.Path], false));
            return true;
        });
        var old = _libraryFolder.Join("old.mkv");
        await File.WriteAllBytesAsync(old, [1, 2, 3]);
        _media.Probes["old.mkv"] = FakeMediaRunner.EnglishAndJapanese;
        await ScanAsync(library);
        return (library, old, _libraryFolder.Join("fresh.mkv"));
    }

    [Fact]
    public async Task The_first_scan_of_a_library_gives_no_file_a_reason()
    {
        var (library, old, _) = await ScannedOnceAsync();

        var reasons = await ReasonsAsync(library);

        Assert.Equal([old], reasons.Keys);
        Assert.Null(reasons[old]);
    }

    [Fact]
    public async Task A_file_that_arrives_after_the_first_scan_is_new_and_stays_new_while_it_still_needs_cleaning()
    {
        var (library, old, fresh) = await ScannedOnceAsync();
        await File.WriteAllBytesAsync(fresh, [9, 9]);
        _media.Probes["fresh.mkv"] = FakeMediaRunner.EnglishAndJapanese;

        await ScanAsync(library);
        await ScanAsync(library);

        var reasons = await ReasonsAsync(library);
        Assert.Equal(LibraryChangeReasons.New, reasons[fresh]);
        Assert.Null(reasons[old]);
    }

    [Fact]
    public async Task A_file_whose_bytes_have_changed_since_the_last_scan_has_been_replaced()
    {
        var (library, old, _) = await ScannedOnceAsync();
        await File.WriteAllBytesAsync(old, [1, 2, 3, 4, 5, 6, 7, 8]);

        await ScanAsync(library);

        Assert.Equal(LibraryChangeReasons.Replaced, (await ReasonsAsync(library))[old]);
    }

    [Fact]
    public async Task A_file_the_last_scan_said_matched_that_now_needs_cleaning_means_the_rules_changed()
    {
        var (library, old, _) = await ScannedOnceAsync();
        await _fixture.Db(uow => uow.ExecuteAsync("UPDATE library_files SET classification = 'matches' WHERE library_id = @id", ("@id", library)));

        await ScanAsync(library);

        Assert.Equal(LibraryChangeReasons.RulesChanged, (await ReasonsAsync(library))[old]);
    }

    [Fact]
    public async Task A_file_that_matches_now_has_no_reason()
    {
        var (library, _, fresh) = await ScannedOnceAsync();
        await File.WriteAllBytesAsync(fresh, [9, 9]);
        _media.Probes["fresh.mkv"] = FakeMediaRunner.EnglishAndJapanese;
        await ScanAsync(library);
        Assert.Equal(LibraryChangeReasons.New, (await ReasonsAsync(library))[fresh]);

        _media.Probes["fresh.mkv"] = FakeMediaRunner.EnglishOnly;
        await File.WriteAllBytesAsync(fresh, [9, 9, 9, 9]);
        await ScanAsync(library);

        Assert.Null((await ReasonsAsync(library))[fresh]);
    }
}
