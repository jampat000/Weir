using Xunit;

namespace Weir.Tray.Tests;

/// <summary>tray-status.json, which the server writes for the tray. A missing file shows no status; one that cannot be read says nothing at all.</summary>
public sealed class TrayStatusFileTests : IDisposable
{
    private readonly TempDirectory _home = TempDirectory.Create();
    private readonly List<string> _log = [];

    public void Dispose() => _home.Dispose();

    private string FilePath => Path.Combine(_home.Path, TrayStatusFile.FileName);

    private TrayStatusReading ReadFile() => TrayStatusFile.Read(_home.Path, _log.Add);

    private TrayStatus? Read() => ReadFile().Status;

    private void Write(string json) => File.WriteAllText(FilePath, json);

    [Fact]
    public void A_full_status_is_read_as_the_server_wrote_it()
    {
        Write("""
            {
              "paused": true,
              "paused_until": "2026-10-09T18:30:00+00:00",
              "unreachable": { "managers": ["Deluno", "Sonarr"], "folders": ["The watched folder for Movies"] },
              "server_ok": true
            }
            """);

        var status = Read();

        Assert.NotNull(status);
        Assert.True(status.Paused);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 18, 30, 0, TimeSpan.Zero), status.PausedUntil);
        Assert.Equal(["Deluno", "Sonarr"], status.ManagersUnreachable);
        Assert.Equal(["The watched folder for Movies"], status.FoldersUnreachable);
        Assert.True(status.ServerOk);
        Assert.Empty(_log);
    }

    [Fact]
    public void A_reading_says_when_the_server_wrote_the_file()
    {
        Write("{}");
        var wrote = new DateTime(2026, 10, 9, 8, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(FilePath, wrote);

        var reading = ReadFile();

        Assert.Equal(wrote, reading.WrittenAtUtc);
        Assert.False(reading.Failed);
    }

    [Fact]
    public void A_status_with_nothing_wrong_has_nothing_not_answering()
    {
        Write("""{ "paused": false, "paused_until": null, "unreachable": { "managers": [], "folders": [] }, "server_ok": true }""");

        var status = Read();

        Assert.NotNull(status);
        Assert.False(status.Paused);
        Assert.Null(status.PausedUntil);
        Assert.False(status.SomethingNotAnswering);
    }

    [Fact]
    public void A_manager_or_a_folder_that_does_not_answer_is_something_not_answering()
    {
        Write("""{ "unreachable": { "managers": ["Deluno"] } }""");
        Assert.True(Read()!.SomethingNotAnswering);

        Write("""{ "unreachable": { "folders": ["The output folder for Movies"] } }""");
        Assert.True(Read()!.SomethingNotAnswering);
    }

    [Fact]
    public void A_count_of_files_in_the_file_is_not_read()
    {
        Write("""{ "needs_you": { "files": 5, "managers_unreachable": ["Deluno"] }, "unreachable": { "files": 7 } }""");

        var status = Read()!;

        Assert.False(status.SomethingNotAnswering);
        Assert.DoesNotContain(typeof(TrayStatus).GetProperties(), property => property.Name.Contains("File", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_file_is_no_status_and_no_complaint()
    {
        var reading = ReadFile();

        Assert.Null(reading.Status);
        Assert.False(reading.Failed);
        Assert.Empty(_log);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"paused\": ")]
    [InlineData("[]")]
    [InlineData("{ \"paused\": \"yes\" }")]
    [InlineData("{ \"unreachable\": { \"managers\": \"Deluno\" } }")]
    public void A_file_that_cannot_be_understood_is_unreadable_and_says_why_in_the_log(string contents)
    {
        Write(contents);

        var reading = ReadFile();

        Assert.True(reading.Failed);
        Assert.Null(reading.Status);
        Assert.Single(_log);
    }

    [Fact]
    public void A_file_holding_only_null_is_no_status_and_not_a_failure()
    {
        Write("null");

        var reading = ReadFile();

        Assert.False(reading.Failed);
        Assert.Null(reading.Status);
    }

    [Fact]
    public void A_file_the_server_has_open_for_writing_can_still_be_read()
    {
        using var server = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        server.Write("""{ "paused": true }"""u8);
        server.Flush();

        var reading = ReadFile();

        Assert.False(reading.Failed);
        Assert.True(reading.Status!.Paused);
    }

    [Fact]
    public void A_file_without_the_optional_parts_reads_as_all_well()
    {
        Write("{}");

        var status = Read();

        Assert.NotNull(status);
        Assert.False(status.Paused);
        Assert.Empty(status.ManagersUnreachable);
        Assert.Empty(status.FoldersUnreachable);
        Assert.True(status.ServerOk);
    }

    [Fact]
    public void A_server_that_says_it_is_not_well_is_read_as_not_ok()
    {
        Write("""{ "server_ok": false }""");

        Assert.False(Read()!.ServerOk);
    }

    [Fact]
    public void Odd_values_are_tidied_not_trusted()
    {
        Write("""{ "unreachable": { "managers": ["  Deluno ", "", "   "], "folders": [" The work folder for Shows", " "] } }""");

        var status = Read()!;

        Assert.Equal(["Deluno"], status.ManagersUnreachable);
        Assert.Equal(["The work folder for Shows"], status.FoldersUnreachable);
    }
}
