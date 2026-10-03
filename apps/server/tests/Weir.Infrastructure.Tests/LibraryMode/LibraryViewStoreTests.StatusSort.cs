using Weir.Core.LibraryMode;
using Weir.Infrastructure.LibraryMode;

namespace Weir.Infrastructure.Tests.LibraryMode;

/// <summary>The Files table sorted by where each file stands now, in the order the statuses read.</summary>
public sealed partial class LibraryViewStoreTests
{
    private const string MatchingProbe = "{\"streams\": [{\"codec_type\": \"video\", \"codec_name\": \"h264\", \"width\": 1920, \"height\": 1080}]}";

    /// <summary>One file in each status, two of them in the status that has an unreadable kind, and some that tie.</summary>
    private async Task SeedFilesInEveryStatus()
    {
        await LibraryAsync();
        await RecordAsync(
            File("/lib/matches-b.mkv", LibraryFileClassification.Matches, MatchingProbe),
            File("/lib/matches-a.mkv", LibraryFileClassification.Matches, MatchingProbe),
            File("/lib/needs-cleaning.mkv", LibraryFileClassification.WouldChange, MatchingProbe),
            File("/lib/cleaning.mkv", LibraryFileClassification.WouldChange, MatchingProbe),
            File("/lib/shared.mkv", LibraryFileClassification.WouldChange, MatchingProbe, linkCount: 2),
            File("/lib/no-video.mkv", LibraryFileClassification.CannotProcess, "", problem: LibraryProblemKind.NoVideo),
            File("/lib/unreadable.mkv", LibraryFileClassification.CannotProcess, "", problem: LibraryProblemKind.Unreadable),
            File("/lib/no-permission.mkv", LibraryFileClassification.CannotProcess, "", problem: LibraryProblemKind.NoPermission),
            File("/lib/left-alone.mkv", LibraryFileClassification.WouldChange, MatchingProbe));
        await _store.WithUnitOfWork(async uow =>
        {
            await uow.ExecuteAsync(
                "INSERT INTO library_file_marks (library_id, path, leave_alone) VALUES (@library, '/lib/left-alone.mkv', 1)",
                ("@library", _libraryId));
            await uow.ExecuteAsync(
                "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, created_at, updated_at) " +
                "VALUES (@key, @kind, '{\"path\": \"/lib/cleaning.mkv\"}', 'pending', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)",
                ("@key", LibraryModeJobKinds.CleanDedupeKey(_libraryId, "/lib/cleaning.mkv")),
                ("@kind", LibraryModeJobKinds.CleanKind));
            return 0;
        });
    }

    [Fact]
    public async Task Files_sorted_by_status_read_matches_then_needs_cleaning_then_cleaning_then_cant_clean_yet_then_left_alone()
    {
        await SeedFilesInEveryStatus();

        var files = await Read(uow => _libraryView.ListFilesAsync(uow, _libraryId, new LibraryFileQuery { Sort = LibraryFileSort.Status }));

        Assert.Equal(
            [
                "/lib/matches-a.mkv", "/lib/matches-b.mkv",
                "/lib/needs-cleaning.mkv",
                "/lib/cleaning.mkv",
                "/lib/no-video.mkv", "/lib/shared.mkv",
                "/lib/no-permission.mkv", "/lib/unreadable.mkv",
                "/lib/left-alone.mkv",
            ],
            files.Select(file => file.Path));
    }

    [Fact]
    public async Task A_file_cant_clean_yet_because_it_cannot_be_read_or_opened_comes_after_the_other_cant_clean_yet_files()
    {
        await SeedFilesInEveryStatus();

        var files = await Read(uow => _libraryView.ListFilesAsync(uow, _libraryId, new LibraryFileQuery { Sort = LibraryFileSort.Status, Status = LibraryFileStatus.CantCleanYet }));

        Assert.Equal(
            ["/lib/no-video.mkv", "/lib/shared.mkv", "/lib/no-permission.mkv", "/lib/unreadable.mkv"],
            files.Select(file => file.Path));
        Assert.All(files, file => Assert.Equal(LibraryFileStatus.CantCleanYet, file.Status));
    }

    [Fact]
    public async Task Descending_reverses_the_order_of_the_statuses_and_keeps_files_of_one_status_in_path_order()
    {
        await SeedFilesInEveryStatus();

        var files = await Read(uow => _libraryView.ListFilesAsync(uow, _libraryId, new LibraryFileQuery { Sort = LibraryFileSort.Status, Descending = true }));

        Assert.Equal(
            [
                "/lib/left-alone.mkv",
                "/lib/no-permission.mkv", "/lib/unreadable.mkv",
                "/lib/no-video.mkv", "/lib/shared.mkv",
                "/lib/cleaning.mkv",
                "/lib/needs-cleaning.mkv",
                "/lib/matches-a.mkv", "/lib/matches-b.mkv",
            ],
            files.Select(file => file.Path));
    }

    [Fact]
    public async Task Paging_by_status_returns_every_file_once()
    {
        await SeedFilesInEveryStatus();
        var query = new LibraryFileQuery { Sort = LibraryFileSort.Status, PageSize = 4 };

        var whole = await Read(uow => _libraryView.ListFilesAsync(uow, _libraryId, query with { PageSize = 100 }));
        var pages = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            pages.AddRange((await Read(uow => _libraryView.ListFilesAsync(uow, _libraryId, query with { Page = page }))).Select(file => file.Path));
        }

        Assert.Equal(whole.Select(file => file.Path), pages);
        Assert.Equal(9, pages.Distinct().Count());
    }

    [Fact]
    public void Status_is_one_of_the_sorts_a_request_can_name()
    {
        Assert.Equal(LibraryFileSort.Status, LibraryFileSort.Normalize("status"));
        Assert.Equal(LibraryFileSort.Path, LibraryFileSort.Normalize("statuses"));
    }
}
