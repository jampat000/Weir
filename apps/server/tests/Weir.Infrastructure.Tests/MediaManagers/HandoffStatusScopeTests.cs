using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>A hand-off's status is worked out from the files it covers, not from whatever else sits in the same folder.</summary>
public sealed class HandoffStatusScopeTests
{
    private const string Folder = "Example.Movie.2024.1080p";
    private const string Main = Folder + "/Example.Movie.2024.1080p.mkv";
    private const string Sample = Folder + "/Sample/example-sample.mkv";

    private static MediaManagerImportEvent Handoff(string path) => new()
    {
        SourceKey = "deluno",
        EventKind = "handoff",
        MediaScope = "movie",
        FilePath = path,
        HandoffId = "release",
        LibraryId = "lib-1",
    };

    private static async Task<(MediaManagerFixture Fixture, long LibraryId, HandoffLedgerRow Row)> ReceiveReleaseFolderAsync()
    {
        var fixture = new MediaManagerFixture();
        var watched = fixture.Store.Home.Join("movies");
        Directory.CreateDirectory(Path.Join(watched, Folder, "Sample"));
        await File.WriteAllTextAsync(Path.Join(watched, Main), "main");
        await File.WriteAllTextAsync(Path.Join(watched, Sample), "sample");
        var libraryId = await fixture.LibraryAsync("movie", watched);
        await fixture.Db(uow => fixture.Intake.EnqueueRefineAsync(uow, Handoff(Path.Join(watched, Folder))));
        var row = (await fixture.Db(uow => HandoffLedgerStore.FindAsync(uow, "deluno", "release")))!;
        return (fixture, libraryId, row);
    }

    [Fact]
    public async Task A_sample_a_scan_recorded_as_waiting_while_processing_was_paused_does_not_hold_the_hand_off_scheduled_once_the_main_file_is_done()
    {
        var (fixture, libraryId, row) = await ReceiveReleaseFolderAsync();
        using (fixture)
        {
            await fixture.Store.Execute(
                $"INSERT INTO files (library_id, relative_path, status, status_reason) VALUES ({libraryId}, '{Sample}', 'out_of_schedule', 'Processing is paused.')");
            await fixture.Store.Execute($"UPDATE jobs SET status = 'completed'; UPDATE files SET status = 'processed' WHERE relative_path = '{Main}'");

            var status = await fixture.Db(uow => fixture.Ledger.CurrentStatusAsync(uow, row));

            Assert.Equal(HandoffLedgerRules.Completed, status.State);
            Assert.Null(status.ScheduledFor);
        }
    }

    [Fact]
    public async Task A_file_the_hand_off_covers_that_is_waiting_still_holds_it_scheduled()
    {
        var (fixture, _, row) = await ReceiveReleaseFolderAsync();
        using (fixture)
        {
            await fixture.Store.Execute($"UPDATE jobs SET status = 'completed'; UPDATE files SET status = 'out_of_schedule' WHERE relative_path = '{Main}'");

            var status = await fixture.Db(uow => fixture.Ledger.CurrentStatusAsync(uow, row));

            Assert.Equal(HandoffLedgerRules.Scheduled, status.State);
        }
    }

    [Fact]
    public async Task The_files_of_a_hand_off_are_the_ones_it_covers()
    {
        var (fixture, libraryId, row) = await ReceiveReleaseFolderAsync();
        using (fixture)
        {
            await fixture.Store.Execute($"INSERT INTO files (library_id, relative_path, status) VALUES ({libraryId}, '{Sample}', 'unprocessed')");

            var files = await fixture.Db(uow => HandoffLedgerStore.FileRowsAsync(uow, row));

            Assert.Equal([Main], files.Select(file => file.RelativePath));
        }
    }
}
