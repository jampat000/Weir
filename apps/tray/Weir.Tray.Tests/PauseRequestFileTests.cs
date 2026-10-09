using System.Text.Json;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The tray's Pause and Resume request, which the server picks up from pause-request.json.</summary>
public sealed class PauseRequestFileTests : IDisposable
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 10, 9, 12, 15, 30, TimeSpan.Zero);

    private readonly TempDirectory _home = TempDirectory.Create();

    public void Dispose() => _home.Dispose();

    private JsonElement Read()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_home.Path, "pause-request.json")));
        return document.RootElement.Clone();
    }

    [Fact]
    public void Pausing_writes_paused_true_and_when_it_was_asked()
    {
        PauseRequestFile.Write(_home.Path, paused: true, RequestedAt);

        var request = Read();
        Assert.True(request.GetProperty("paused").GetBoolean());
        Assert.Equal(RequestedAt, request.GetProperty("requested_at").GetDateTimeOffset());
        Assert.Equal(2, request.EnumerateObject().Count());
    }

    [Fact]
    public void Resuming_writes_paused_false()
    {
        PauseRequestFile.Write(_home.Path, paused: false, RequestedAt);

        Assert.False(Read().GetProperty("paused").GetBoolean());
    }

    [Fact]
    public void The_time_is_written_as_an_iso_8601_text()
    {
        PauseRequestFile.Write(_home.Path, paused: true, RequestedAt);

        Assert.Equal("2026-10-09T12:15:30+00:00", Read().GetProperty("requested_at").GetString());
    }

    [Fact]
    public void A_later_request_replaces_the_earlier_one_and_leaves_nothing_else_behind()
    {
        PauseRequestFile.Write(_home.Path, paused: true, RequestedAt);
        PauseRequestFile.Write(_home.Path, paused: false, RequestedAt.AddMinutes(1));

        Assert.False(Read().GetProperty("paused").GetBoolean());
        Assert.Equal(["pause-request.json"], Directory.GetFileSystemEntries(_home.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void The_data_folder_is_made_if_it_is_not_there_yet()
    {
        var nested = Path.Combine(_home.Path, "not", "yet");

        PauseRequestFile.Write(nested, paused: true, RequestedAt);

        Assert.True(File.Exists(Path.Combine(nested, "pause-request.json")));
    }
}
