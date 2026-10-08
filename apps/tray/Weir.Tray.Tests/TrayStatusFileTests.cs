using Xunit;

namespace Weir.Tray.Tests;

/// <summary>tray-status.json, which the server writes for the tray. A missing or damaged file shows no status, never a wrong one.</summary>
public sealed class TrayStatusFileTests : IDisposable
{
    private readonly TempDirectory _home = TempDirectory.Create();
    private readonly List<string> _log = [];

    public void Dispose() => _home.Dispose();

    private TrayStatus? Read() => TrayStatusFile.Read(_home.Path, _log.Add);

    private void Write(string json) => File.WriteAllText(Path.Combine(_home.Path, TrayStatusFile.FileName), json);

    [Fact]
    public void A_full_status_is_read_as_the_server_wrote_it()
    {
        Write("""
            {
              "paused": true,
              "paused_until": "2026-10-09T18:30:00+00:00",
              "needs_you": { "files": 2, "managers_unreachable": ["Deluno", "Sonarr"] },
              "server_ok": true
            }
            """);

        var status = Read();

        Assert.NotNull(status);
        Assert.True(status.Paused);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 18, 30, 0, TimeSpan.Zero), status.PausedUntil);
        Assert.Equal(2, status.FilesNeedingYou);
        Assert.Equal(["Deluno", "Sonarr"], status.ManagersUnreachable);
        Assert.True(status.ServerOk);
        Assert.Empty(_log);
    }

    [Fact]
    public void A_status_with_nothing_wrong_needs_nothing()
    {
        Write("""{ "paused": false, "paused_until": null, "needs_you": { "files": 0, "managers_unreachable": [] }, "server_ok": true }""");

        var status = Read();

        Assert.NotNull(status);
        Assert.False(status.Paused);
        Assert.Null(status.PausedUntil);
        Assert.False(status.NeedsYou);
    }

    [Fact]
    public void A_missing_file_is_no_status_and_no_complaint()
    {
        Assert.Null(Read());
        Assert.Empty(_log);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"paused\": ")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{ \"paused\": \"yes\" }")]
    [InlineData("{ \"needs_you\": { \"files\": \"many\" } }")]
    public void A_file_that_cannot_be_understood_is_no_status_and_says_why_in_the_log(string contents)
    {
        Write(contents);

        Assert.Null(Read());
        if (contents != "null")
        {
            Assert.Single(_log);
        }
    }

    [Fact]
    public void A_file_without_the_optional_parts_reads_as_all_well()
    {
        Write("{}");

        var status = Read();

        Assert.NotNull(status);
        Assert.False(status.Paused);
        Assert.Equal(0, status.FilesNeedingYou);
        Assert.Empty(status.ManagersUnreachable);
        Assert.True(status.ServerOk);
    }

    [Fact]
    public void A_server_that_says_it_is_not_well_needs_the_person()
    {
        Write("""{ "server_ok": false }""");

        Assert.True(Read()!.NeedsYou);
    }

    [Fact]
    public void Odd_values_are_tidied_not_trusted()
    {
        Write("""{ "needs_you": { "files": -3, "managers_unreachable": ["  Deluno ", "", "   "] } }""");

        var status = Read()!;

        Assert.Equal(0, status.FilesNeedingYou);
        Assert.Equal(["Deluno"], status.ManagersUnreachable);
    }
}
