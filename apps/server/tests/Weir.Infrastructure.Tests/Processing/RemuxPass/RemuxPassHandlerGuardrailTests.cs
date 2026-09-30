using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.Processing.RemuxPass;

/// <summary>
/// What a pass records when a guardrail stops it: a full output drive leaves the file waiting, and a file under the minimum
/// size is skipped. Neither may ever be recorded as done.
/// </summary>
public sealed partial class RemuxPassHandlerTests
{
    private const long BytesPerMb = 1024L * 1024;
    private const long KeepFreeMb = 2048;

    private static string PassPayload(long library) =>
        $$"""{"relative_media_path":"Movie/file.mkv","media_scope":"movie","library_id":{{library}}}""";

    private async Task<long> LibraryWithOneFileAsync()
    {
        var library = await LibraryAsync();
        _folders.Source(Path.Join("Movie", "file.mkv"));
        _media.Probes["file.mkv"] = FakeMediaRunner.EnglishAndJapanese;
        await FileRowAsync(library, "Movie/file.mkv", ProcessingFileStatuses.Unprocessed);
        return library;
    }

    private Task KeepFreeOnOutputDriveAsync() =>
        _fixture.Store.Execute($"UPDATE operator_settings SET minimum_free_disk_space_mb = {KeepFreeMb}");

    [Fact]
    public async Task A_file_waits_when_the_output_drive_has_less_than_the_space_to_keep_free()
    {
        var library = await LibraryWithOneFileAsync();
        await KeepFreeOnOutputDriveAsync();

        await Handler(freeBytes: _ => 512 * BytesPerMb).HandleAsync(Context(1, PassPayload(library)), CancellationToken.None);

        Assert.Equal(ProcessingFileStatuses.OnHold, await ScalarText("SELECT status FROM files"));
        Assert.StartsWith("Waiting: the output drive has less than 2.0 GB free", await ScalarText("SELECT status_reason FROM files"), StringComparison.Ordinal);
        Assert.False(File.Exists(_folders.Out("Movie/file.mkv")));
    }

    [Fact]
    public async Task A_file_is_not_read_through_while_the_output_drive_has_no_room()
    {
        var library = await LibraryWithOneFileAsync();
        await KeepFreeOnOutputDriveAsync();

        await Handler(freeBytes: _ => 512 * BytesPerMb).HandleAsync(Context(1, PassPayload(library)), CancellationToken.None);

        Assert.Empty(_media.Calls);
    }

    [Fact]
    public async Task A_file_waiting_for_room_is_looked_at_again_and_finishes_once_space_is_free()
    {
        var library = await LibraryWithOneFileAsync();
        await KeepFreeOnOutputDriveAsync();
        await Handler(freeBytes: _ => 512 * BytesPerMb).HandleAsync(Context(1, PassPayload(library)), CancellationToken.None);

        var booked = await ScalarText($"SELECT payload_json FROM jobs WHERE job_kind = '{RemuxPassOutcomes.JobKind}' AND not_before IS NOT NULL");
        Assert.Equal("Movie/file.mkv", WireConvert.Str(((WireObject)WireJsonParser.Parse(booked))["relative_media_path"]));
        await Handler(freeBytes: _ => 10_000 * BytesPerMb).HandleAsync(Context(2, booked), CancellationToken.None);

        Assert.Equal(ProcessingFileStatuses.Processed, await ScalarText("SELECT status FROM files"));
        Assert.True(File.Exists(_folders.Out("Movie/file.mkv")));
    }

    [Fact]
    public async Task A_file_the_output_drive_can_hold_is_processed_as_usual()
    {
        var library = await LibraryWithOneFileAsync();
        await KeepFreeOnOutputDriveAsync();

        await Handler(freeBytes: _ => 10_000 * BytesPerMb).HandleAsync(Context(1, PassPayload(library)), CancellationToken.None);

        Assert.Equal(ProcessingFileStatuses.Processed, await ScalarText("SELECT status FROM files"));
    }

    [Fact]
    public async Task A_file_under_the_minimum_size_reaching_the_pass_is_recorded_as_skipped_for_its_size()
    {
        var library = await LibraryWithOneFileAsync();
        await _fixture.Store.Execute("UPDATE libraries SET min_file_size_mb = 1");

        await Handler().HandleAsync(Context(1, PassPayload(library)), CancellationToken.None);

        Assert.Equal(ProcessingFileStatuses.Skipped, await ScalarText("SELECT status FROM files"));
        Assert.StartsWith("Skipped because this file is 0.0 MB, under the 1 MB minimum.", await ScalarText("SELECT status_reason FROM files"), StringComparison.Ordinal);
        Assert.False(File.Exists(_folders.Out("Movie/file.mkv")));
    }
}
