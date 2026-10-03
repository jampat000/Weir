using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>
/// A file's history is kept for as long as Weir still knows the file, then for the Activity page's number of days after the
/// file is gone or forgotten. The days count from when Weir first sees the history without its file, so a file forgotten long
/// after it was processed still keeps its history for the whole period.
/// </summary>
public sealed class FileHistoryRetentionTests : IDisposable
{
    private const int RetentionDays = 30;
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly JobsTestDatabase _db = new();
    private readonly FileLogStore _fileLogs = new();

    public FileHistoryRetentionTests()
    {
        _db.Execute("INSERT INTO libraries (id, name, media_type, watched_folder, display_order) VALUES (1, 'Movies', 'movie', '/in', 1), (2, 'TV', 'tv', '/tv', 2)");
    }

    public void Dispose() => _db.Dispose();

    private void KnownFile(long library, string path) =>
        _db.Execute($"INSERT INTO files (library_id, relative_path) VALUES ({library}, '{path}')");

    private void ForgetFile(long library, string path) =>
        _db.Execute($"DELETE FROM files WHERE library_id = {library} AND relative_path = '{path}'");

    private void History(long? library, string path, DateTimeOffset recordedAt) =>
        _db.Execute(
            "INSERT INTO file_logs (library_id, relative_path, recorded_at) VALUES (@library, @path, @recorded)",
            ("@library", library), ("@path", path), ("@recorded", recordedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture)));

    private Task<int> PruneAsync(DateTimeOffset at, long days = RetentionDays) => _fileLogs.PruneAsync(_db.Database, days, at);

    private string KeptPaths() => _db.Scalar("SELECT coalesce(group_concat(relative_path, ','), '') FROM (SELECT relative_path FROM file_logs ORDER BY relative_path)")?.ToString() ?? string.Empty;

    [Fact]
    public async Task History_of_a_file_Weir_still_knows_is_kept_however_old_it_is()
    {
        KnownFile(1, "film.mkv");
        History(1, "film.mkv", Now.AddDays(-400));

        var removed = await PruneAsync(Now.AddDays(1000));

        Assert.Equal(0, removed);
        Assert.Equal("film.mkv", KeptPaths());
    }

    [Fact]
    public async Task History_of_a_file_that_is_gone_is_kept_until_the_days_have_passed_since_Weir_noticed()
    {
        KnownFile(1, "film.mkv");
        History(1, "film.mkv", Now.AddDays(-400));
        ForgetFile(1, "film.mkv");

        Assert.Equal(0, await PruneAsync(Now));
        Assert.Equal(0, await PruneAsync(Now.AddDays(RetentionDays - 1)));
        Assert.Equal("film.mkv", KeptPaths());

        Assert.Equal(1, await PruneAsync(Now.AddDays(RetentionDays + 1)));
        Assert.Equal(string.Empty, KeptPaths());
    }

    [Fact]
    public async Task A_gone_files_history_is_removed_whole_and_another_files_is_untouched()
    {
        KnownFile(1, "gone.mkv");
        KnownFile(1, "kept.mkv");
        History(1, "gone.mkv", Now.AddDays(-90));
        History(1, "gone.mkv", Now.AddDays(-80));
        History(1, "kept.mkv", Now.AddDays(-90));
        ForgetFile(1, "gone.mkv");
        await PruneAsync(Now);

        var removed = await PruneAsync(Now.AddDays(RetentionDays + 1));

        Assert.Equal(2, removed);
        Assert.Equal("kept.mkv", KeptPaths());
    }

    [Fact]
    public async Task A_file_that_comes_back_before_the_days_are_up_keeps_its_history()
    {
        KnownFile(1, "film.mkv");
        History(1, "film.mkv", Now.AddDays(-10));
        ForgetFile(1, "film.mkv");
        await PruneAsync(Now);

        KnownFile(1, "film.mkv");
        await PruneAsync(Now.AddDays(1));
        var removed = await PruneAsync(Now.AddDays(RetentionDays * 3));

        Assert.Equal(0, removed);
        Assert.Equal("film.mkv", KeptPaths());
    }

    [Fact]
    public async Task A_gone_file_gets_a_full_period_again_when_it_is_forgotten_a_second_time()
    {
        KnownFile(1, "film.mkv");
        History(1, "film.mkv", Now.AddDays(-10));
        ForgetFile(1, "film.mkv");
        await PruneAsync(Now);
        KnownFile(1, "film.mkv");
        await PruneAsync(Now.AddDays(RetentionDays - 1));
        ForgetFile(1, "film.mkv");
        await PruneAsync(Now.AddDays(RetentionDays));

        var removed = await PruneAsync(Now.AddDays(RetentionDays + 5));

        Assert.Equal(0, removed);
        Assert.Equal("film.mkv", KeptPaths());
    }

    [Fact]
    public async Task The_same_path_in_another_workflow_does_not_keep_a_gone_files_history()
    {
        KnownFile(2, "film.mkv");
        History(1, "film.mkv", Now.AddDays(-5));

        await PruneAsync(Now);
        var removed = await PruneAsync(Now.AddDays(RetentionDays + 1));

        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task History_left_by_a_deleted_workflow_is_kept_only_while_some_workflow_still_knows_the_path()
    {
        KnownFile(2, "shared.mkv");
        History(null, "shared.mkv", Now.AddDays(-50));
        History(null, "alone.mkv", Now.AddDays(-50));

        await PruneAsync(Now);
        var removed = await PruneAsync(Now.AddDays(RetentionDays + 1));

        Assert.Equal(1, removed);
        Assert.Equal("shared.mkv", KeptPaths());
    }

    [Fact]
    public async Task Zero_days_keeps_every_record_even_for_a_file_that_is_gone()
    {
        History(1, "gone.mkv", Now.AddDays(-900));

        var removed = await PruneAsync(Now.AddDays(5000), days: 0);

        Assert.Equal(0, removed);
        Assert.Equal("gone.mkv", KeptPaths());
    }

    [Fact]
    public async Task A_file_that_went_while_history_was_kept_forever_counts_its_days_from_when_it_went()
    {
        History(1, "gone.mkv", Now.AddDays(-900));
        await PruneAsync(Now, days: 0);

        Assert.Equal(0, await PruneAsync(Now.AddDays(RetentionDays - 1)));
        Assert.Equal(1, await PruneAsync(Now.AddDays(RetentionDays + 1)));
    }

    [Fact]
    public async Task A_new_install_keeps_a_gone_files_history_for_ninety_days()
    {
        var settings = new OperatorSettingsStore();
        var uow = await UnitOfWork.OpenAsync(_db.Database);
        await using (uow)
        {
            var row = await settings.EnsureAsync(uow);
            await uow.CommitAsync();

            Assert.Equal(90, row.FileLogRetentionDays);
        }
    }
}
