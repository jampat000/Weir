using System.Text.Json.Nodes;

using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// Before the tray applies an update it has the still-running server save a copy of Weir's data, through
/// update-backup-request.json and update-backup-result.json (#951). An update whose copy could not be saved is not applied, and
/// the version that is running keeps running. Time is fake and the server's answers are files written at chosen moments.
/// </summary>
public sealed class UpdateBackupTests : IDisposable
{
    private const string Target = "1.0.0-rc.13";
    private const string DiskFull = "Drive C: needs about 480 MB free to hold the copy and has 120 MB. Free up space there, then try again.";

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly DelayWatchingTimeProvider _clock = new();
    private readonly StandInServers _servers = new();
    private bool _serverRuns = true;

    public void Dispose()
    {
        _servers.Dispose();
        _home.Dispose();
    }

    private string RequestPath => Path.Combine(_home.Path, UpdateBackupRequest.RequestFileName);

    private string ResultPath => Path.Combine(_home.Path, UpdateBackupRequest.ResultFileName);

    private UpdateBackupRequest Request() => new(_home.Path, _clock, () => _serverRuns);

    private string AskedId() => (string)JsonNode.Parse(File.ReadAllText(RequestPath))!["id"]!;

    private void ServerWrites(string id, string state, string? reason = null) =>
        File.WriteAllText(
            ResultPath,
            new JsonObject { ["id"] = id, ["state"] = state, ["reason"] = reason }.ToJsonString());

    // The request is written and the tray is parked on its first look at the answer.
    private async Task<(Task<UpdateBackupAnswer> Answer, string Id)> AskAsync()
    {
        var answer = Request().AskAsync(Target, CancellationToken.None);
        await _clock.NextDelay();
        return (answer, AskedId());
    }

    private async Task StepAsync(Task<UpdateBackupAnswer> answer)
    {
        _clock.Advance(UpdateBackupRequest.PollInterval);
        if (!answer.IsCompleted)
        {
            await Task.WhenAny(answer, _clock.NextDelay());
        }
    }

    [Fact(Timeout = 10_000)]
    public async Task The_request_names_the_version_the_update_is_to_and_when_it_was_made()
    {
        var (answer, id) = await AskAsync();

        var request = JsonNode.Parse(File.ReadAllText(RequestPath))!;
        Assert.Equal(Target, (string?)request["target_version"]);
        Assert.Equal(_clock.GetUtcNow(), DateTimeOffset.Parse((string)request["requested_at"]!, System.Globalization.CultureInfo.InvariantCulture));
        ServerWrites(id, "saved");
        await StepAsync(answer);
        Assert.Equal(UpdateBackupOutcome.Saved, (await answer).Outcome);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_server_that_says_it_is_saving_and_then_that_it_saved_is_waited_for()
    {
        var (answer, id) = await AskAsync();

        ServerWrites(id, "started");
        await StepAsync(answer);
        Assert.False(answer.IsCompleted);
        ServerWrites(id, "saved");
        await StepAsync(answer);

        Assert.Equal(new UpdateBackupAnswer(UpdateBackupOutcome.Saved, null), await answer);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_server_that_could_not_save_the_copy_says_why_in_its_own_words()
    {
        var (answer, id) = await AskAsync();

        ServerWrites(id, "started");
        await StepAsync(answer);
        ServerWrites(id, "failed", DiskFull);
        await StepAsync(answer);

        Assert.Equal(new UpdateBackupAnswer(UpdateBackupOutcome.Failed, DiskFull), await answer);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_server_that_never_answers_is_an_older_build_and_the_request_is_taken_back()
    {
        var (answer, _) = await AskAsync();

        _clock.Advance(UpdateBackupRequest.HearingTime);

        Assert.Equal(new UpdateBackupAnswer(UpdateBackupOutcome.NotAnswered, null), await answer);
        Assert.False(File.Exists(RequestPath));
    }

    [Fact(Timeout = 10_000)]
    public async Task An_answer_to_an_earlier_request_is_not_taken_for_this_ones()
    {
        var (answer, _) = await AskAsync();

        ServerWrites("an-earlier-request", "saved");
        _clock.Advance(UpdateBackupRequest.HearingTime);

        Assert.Equal(UpdateBackupOutcome.NotAnswered, (await answer).Outcome);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_server_that_stops_while_it_is_saving_is_a_failed_copy()
    {
        var (answer, id) = await AskAsync();

        ServerWrites(id, "started");
        _serverRuns = false;
        await StepAsync(answer);

        var result = await answer;
        Assert.Equal(UpdateBackupOutcome.Failed, result.Outcome);
        Assert.Contains("Weir stopped while it was saving the copy", result.Reason, StringComparison.Ordinal);
    }

    [Fact(Timeout = 10_000)]
    public async Task A_copy_that_never_finishes_is_a_failed_copy()
    {
        var (answer, id) = await AskAsync();

        ServerWrites(id, "started");
        await StepAsync(answer);
        _clock.Advance(UpdateBackupRequest.LongestSave);

        var result = await answer;
        Assert.Equal(UpdateBackupOutcome.Failed, result.Outcome);
        Assert.Contains("taking too long", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_server_running_there_is_nothing_to_ask_and_the_update_may_go_ahead()
    {
        var asked = false;
        var hook = Hook(
            (_, _) =>
            {
                asked = true;
                return Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Saved, null));
            },
            running: false);

        Assert.True(await hook.SaveBeforeApplyAsync(Target));
        Assert.False(asked);
    }

    [Fact]
    public async Task A_saved_copy_lets_the_update_go_ahead_and_forgets_an_earlier_failure()
    {
        File.WriteAllText(Path.Combine(_home.Path, "update-backup-failed"), Target);
        var hook = Hook((_, _) => Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Saved, null)));

        Assert.True(await hook.SaveBeforeApplyAsync(Target));
        Assert.False(UpdateBackupHook.FailedFor(_home.Path, Target));
    }

    [Fact]
    public async Task A_server_too_old_to_answer_leaves_the_copy_to_the_server_that_starts_after_the_update()
    {
        var hook = Hook((_, _) => Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.NotAnswered, null)));

        Assert.True(await hook.SaveBeforeApplyAsync(Target));
    }

    [Fact]
    public async Task A_copy_that_could_not_be_saved_stops_the_update_and_says_why()
    {
        var told = new List<string>();
        var hook = Hook((_, _) => Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Failed, DiskFull)), told.Add);

        Assert.False(await hook.SaveBeforeApplyAsync(Target));
        Assert.Equal([DiskFull], told);
        Assert.True(UpdateBackupHook.FailedFor(_home.Path, Target));
        Assert.False(UpdateBackupHook.FailedFor(_home.Path, "1.0.0-rc.14"));
    }

    [Fact]
    public async Task A_data_folder_that_cannot_be_written_is_a_failed_copy_not_a_crash()
    {
        var told = new List<string>();
        var hook = Hook((_, _) => throw new IOException("no"), told.Add);

        Assert.False(await hook.SaveBeforeApplyAsync(Target));
        Assert.Single(told);
        Assert.StartsWith("Weir couldn't ask itself to save the copy", told[0], StringComparison.Ordinal);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_restart_to_update_whose_copy_failed_applies_nothing_and_the_old_version_keeps_running()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var update = new FakeUpdateService().AlreadyDownloaded();
        var told = new List<string>();
        var backup = Hook((_, _) => Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Failed, DiskFull)), told.Add);

        await new TrayShutdown(() => ServerProcessStop.StopAsync(server), update, backup).RestartToUpdateAsync();

        Assert.Empty(update.Applied);
        Assert.False(server.HasExited);
        Assert.Equal([DiskFull], told);
    }

    [Fact(Timeout = 60_000)]
    public async Task Quitting_whose_copy_failed_stops_Weir_but_leaves_the_update_waiting()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var update = new FakeUpdateService().AlreadyDownloaded();
        var backup = Hook((_, _) => Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Failed, DiskFull)));

        await new TrayShutdown(() => ServerProcessStop.StopAsync(server), update, backup).QuitAsync();

        Assert.Empty(update.Applied);
        Assert.True(server.HasExited);
    }

    [Fact(Timeout = 60_000)]
    public async Task The_copy_is_asked_for_while_the_server_still_runs_and_before_the_update_is_applied()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var update = new FakeUpdateService().AlreadyDownloaded();
        var order = new List<string>();
        var serverRanWhenAsked = false;
        string? versionAsked = null;
        update.OnApply = call => order.Add(call);
        var backup = Hook(
            (version, _) =>
            {
                serverRanWhenAsked = !server.HasExited;
                versionAsked = version;
                order.Add("copy saved");
                return Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Saved, null));
            });

        await new TrayShutdown(() => ServerProcessStop.StopAsync(server), update, backup).RestartToUpdateAsync();

        Assert.True(serverRanWhenAsked);
        Assert.Equal(update.PendingVersion, versionAsked);
        Assert.Equal(["copy saved", FakeUpdateService.AppliedAndRestarted], order);
        Assert.True(server.HasExited);
    }

    [Fact(Timeout = 60_000)]
    public async Task Nothing_is_asked_for_when_no_update_is_downloaded()
    {
        var server = await _servers.StartAsync(ServerFolder());
        var asked = false;
        var backup = Hook((_, _) =>
        {
            asked = true;
            return Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Saved, null));
        });

        await new TrayShutdown(() => ServerProcessStop.StopAsync(server), new FakeUpdateService(), backup).QuitAsync();

        Assert.False(asked);
    }

    [Fact]
    public void An_update_whose_copy_failed_is_not_applied_at_start_up_where_no_server_runs_to_ask()
    {
        File.WriteAllText(Path.Combine(_home.Path, "update-backup-failed"), "3.3.0");
        var service = new FakeUpdateService { LeftWaitingVersion = "3.3.0" };

        var handedOver = UpdateOnStart.TryApply(service, UpdateMode.Auto, _home.Path, () => { });

        Assert.False(handedOver);
        Assert.Empty(service.Applied);
    }

    [Fact]
    public void A_different_update_left_waiting_is_applied_at_start_up_as_before()
    {
        File.WriteAllText(Path.Combine(_home.Path, "update-backup-failed"), "3.2.0");
        var service = new FakeUpdateService { LeftWaitingVersion = "3.3.0" };

        Assert.True(UpdateOnStart.TryApply(service, UpdateMode.Auto, _home.Path, () => { }));
        Assert.Equal([FakeUpdateService.AppliedLeftWaiting], service.Applied);
    }

    [Fact]
    public async Task The_reason_is_kept_for_System_About_until_a_copy_is_saved_or_the_tray_starts_again()
    {
        var reasonPath = Path.Combine(_home.Path, "update-not-applied.json");
        var failing = Hook((_, _) => Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Failed, DiskFull)));

        await failing.SaveBeforeApplyAsync(Target);

        var held = JsonNode.Parse(await File.ReadAllTextAsync(reasonPath))!;
        Assert.Equal(DiskFull, (string?)held["reason"]);
        Assert.Equal(Target, (string?)held["version"]);

        // A tray that starts has applied nothing yet.
        UpdateBackupHook.ForgetReason(_home.Path);
        Assert.False(File.Exists(reasonPath));

        await failing.SaveBeforeApplyAsync(Target);
        Assert.True(File.Exists(reasonPath));

        await Hook((_, _) => Task.FromResult(new UpdateBackupAnswer(UpdateBackupOutcome.Saved, null))).SaveBeforeApplyAsync(Target);
        Assert.False(File.Exists(reasonPath));
    }

    private UpdateBackupHook Hook(
        Func<string, CancellationToken, Task<UpdateBackupAnswer>> ask, Action<string>? onNotUpdated = null, bool running = true) =>
        new(ask, _home.Path, () => running, onNotUpdated ?? (_ => { }));

    private string ServerFolder() => Path.Combine(_home.Path, "server-" + Guid.NewGuid().ToString("n"));
}
