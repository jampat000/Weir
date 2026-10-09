using System.Text.Json;
using Microsoft.Extensions.Logging;
using Weir.Core.Processing;
using Weir.Core.Settings;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Auth;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>The tray learns what to show from <c>tray-status.json</c>: the pause, the files that wait on a person, the managers that do not answer, and whether the server is running.</summary>
public sealed class TrayStatusWriterTests : IAsyncLifetime, IDisposable
{
    private readonly StoreFixture _store = new();
    private readonly DataChangePublisher _changes = new();
    private readonly SuitePauseService _pause;
    private readonly CapturingLogger<TrayStatusWriter> _log = new();
    private readonly TrayStatusWriter _writer;

    public TrayStatusWriterTests()
    {
        var settings = new SuiteSettingsStore(new AuthStore(), _changes);
        _pause = new SuitePauseService(settings, new ActivityStore(), _changes);
        _writer = new TrayStatusWriter(_store.Options, _store.Database, settings, new FileStateStore(), new MediaManagerConnectionStore(), _changes, _store.Clock, _log);
    }

    private string StatusPath => Path.Join(_store.Options.WeirHome, TrayStatus.FileName);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _writer.StopAsync(CancellationToken.None);

    public void Dispose()
    {
        _writer.Dispose();
        _store.Dispose();
    }

    private Task StartAsync() => _writer.StartAsync(CancellationToken.None);

    private JsonElement? Status()
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(StatusPath)).RootElement.Clone();
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private Task UntilAsync(Func<JsonElement, bool> condition) =>
        Eventually.ThatAsync(() => Status() is { } status && condition(status));

    private Task<PauseState> PauseAsync(bool paused, long? minutes = null) =>
        _store.WithUnitOfWork(uow => _pause.ChangeAsync(uow, paused, minutes, keepEnd: false, true, Timestamp.UtcNow(_store.Clock), "alice"));

    private async Task SeedFilesAsync(params (string Status, string Reason, bool Held)[] files)
    {
        var library = await _store.Scalar("INSERT INTO libraries (name, media_type) VALUES ('Films', 'movie') RETURNING id");
        for (var i = 0; i < files.Length; i++)
        {
            var (status, reason, held) = files[i];
            await _store.Execute(
                "INSERT INTO files (library_id, relative_path, status, status_reason, hold_until, last_seen_at) " +
                $"VALUES ({library}, 'Film {i}/film.mkv', '{status}', '{reason}', {(held ? "'2026-01-15 12:00:00'" : "NULL")}, CURRENT_TIMESTAMP)");
        }
    }

    [Fact]
    public async Task The_status_is_written_when_the_server_starts_saying_all_is_well()
    {
        await StartAsync();
        await UntilAsync(_ => true);

        Assert.Equal(
            "{\"paused\":false,\"paused_until\":null,\"needs_you\":{\"files\":0,\"managers_unreachable\":[]},\"server_ok\":true}",
            File.ReadAllText(StatusPath));
    }

    [Fact]
    public async Task Pausing_and_resuming_are_written()
    {
        await StartAsync();
        await UntilAsync(_ => true);

        await PauseAsync(paused: true, minutes: 30);
        await UntilAsync(status => status.GetProperty("paused").GetBoolean());
        Assert.Equal("2026-01-15T10:30:00Z", Status()!.Value.GetProperty("paused_until").GetString());

        await PauseAsync(paused: false);
        await UntilAsync(status => !status.GetProperty("paused").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Status()!.Value.GetProperty("paused_until").ValueKind);
    }

    [Fact]
    public async Task Files_that_wait_on_a_person_are_counted_and_the_rest_are_not()
    {
        await StartAsync();
        await UntilAsync(_ => true);

        await SeedFilesAsync(
            ("processing_failed", "Weir could not read the audio track.", false),
            ("rejected", "Nothing was left to keep.", false),
            ("on_hold", "Waiting for a decision.", false),
            ("skipped", "Skipped because Weir leaves files like this alone.", false),
            ("skipped", "Skipped because this file is 3.0 MB, under the 10 MB minimum. Weir deleted it.", false),
            ("skipped", "Skipped because this file is 90.0 MB and exceeds the 50 MB workflow maximum.", false),
            ("on_hold", "Waiting until the file stops changing.", true),
            ("skipped", "Already in the format you keep.", false),
            ("processed", "Finished processing this file.", false));
        // The code, not the sentence, says this skip is the workflow's minimum size doing its job.
        await _store.Execute($"UPDATE files SET skip_kind = '{SkipKinds.BelowMinimumSize}' WHERE status_reason = 'Skipped because Weir leaves files like this alone.'");
        await _store.Execute($"UPDATE files SET skip_kind = '{SkipKinds.BelowMinimumSizeRemoved}' WHERE status_reason LIKE '%Weir deleted it.'");
        _changes.Publish(DataTopics.Jobs);

        await UntilAsync(status => status.GetProperty("needs_you").GetProperty("files").GetInt64() == 4);
    }

    [Fact]
    public async Task Enabled_managers_that_do_not_answer_are_named()
    {
        await StartAsync();
        await UntilAsync(_ => true);

        await _store.Execute(
            "INSERT INTO media_manager_connections (kind, name, enabled, base_url, last_connection_test_ok) VALUES " +
            "('deluno', 'Deluno on RIG', 1, 'http://192.0.2.10:5000', 0), " +
            "('radarr', 'Radarr on nas', 1, 'http://192.0.2.20:7878', 1), " +
            "('sonarr', 'Sonarr on nas', 1, 'http://192.0.2.20:8989', NULL), " +
            "('native', 'Native on box', 0, 'http://192.0.2.30:5000', 0)");
        _changes.Publish(DataTopics.Connections);

        await UntilAsync(status => status.GetProperty("needs_you").GetProperty("managers_unreachable").GetArrayLength() == 1);
        Assert.Equal("Deluno on RIG", Status()!.Value.GetProperty("needs_you").GetProperty("managers_unreachable")[0].GetString());
    }

    [Fact]
    public async Task A_change_that_leaves_the_status_as_it_was_does_not_rewrite_the_file()
    {
        await StartAsync();
        await UntilAsync(_ => true);
        var written = File.GetLastWriteTimeUtc(StatusPath);

        _changes.Publish(DataTopics.Jobs);
        _changes.Publish(DataTopics.Connections);
        await Task.Delay(TrayStatusWriter.Settle * 3);

        Assert.Equal(written, File.GetLastWriteTimeUtc(StatusPath));
        Assert.Empty(Directory.GetFiles(_store.Options.WeirHome, ".tray-status.json.*.tmp"));
    }

    [Fact]
    public async Task Stopping_the_server_says_it_is_no_longer_running_and_keeps_the_rest()
    {
        await StartAsync();
        await UntilAsync(_ => true);
        await PauseAsync(paused: true);
        await UntilAsync(status => status.GetProperty("paused").GetBoolean());

        await _writer.StopAsync(CancellationToken.None);

        var status = Status()!.Value;
        Assert.False(status.GetProperty("server_ok").GetBoolean());
        Assert.True(status.GetProperty("paused").GetBoolean());
    }

    [Fact]
    public async Task A_reading_that_fails_is_tried_again_so_an_old_file_never_stays_on_show()
    {
        await PauseAsync(paused: false);
        await File.WriteAllTextAsync(StatusPath, "{\"paused\":true,\"paused_until\":null,\"needs_you\":{\"files\":9,\"managers_unreachable\":[]},\"server_ok\":true}");
        await _store.Execute("UPDATE suite_settings SET processing_paused_until = 'not a time' WHERE id = 1");

        await StartAsync();
        await Eventually.ThatAsync(() => _log.Logged(LogLevel.Warning, "could not read what the tray shows"));
        Assert.True(Status()!.Value.GetProperty("paused").GetBoolean());

        await _store.Execute("UPDATE suite_settings SET processing_paused_until = NULL WHERE id = 1");
        await UntilAsync(status => !status.GetProperty("paused").GetBoolean());
        Assert.Equal(0, Status()!.Value.GetProperty("needs_you").GetProperty("files").GetInt64());
    }

    [WindowsFact("A file that is open cannot be replaced only where file sharing is enforced.")]
    public async Task A_file_the_tray_is_reading_is_replaced_once_it_lets_go()
    {
        await StartAsync();
        await UntilAsync(_ => true);

        using (new FileStream(StatusPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await PauseAsync(paused: true);
            await Eventually.ThatAsync(() => _log.Logged(LogLevel.Warning, "could not tell the tray how it is"));
            Assert.False(Status()!.Value.GetProperty("paused").GetBoolean());
        }

        await UntilAsync(status => status.GetProperty("paused").GetBoolean());
    }
}
