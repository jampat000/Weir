using Microsoft.Extensions.Logging;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// The tray's dot goes amber for a watched, work or output folder Weir cannot reach, so Weir looks at those folders itself,
/// browser or no browser, and says so on the folder-checks topic when its answer changes.
/// </summary>
public sealed class FolderReachabilityTests : IAsyncLifetime, IDisposable
{
    private sealed class Probe : IFolderProbe
    {
        public HashSet<string> Existing { get; } = [];

        public HashSet<string> Unreadable { get; } = [];

        public bool Fails { get; set; }

        public bool Exists(string path) => Fails ? throw new InvalidOperationException("The share did not answer.") : Existing.Contains(path);

        public bool CanRead(string path) => !Unreadable.Contains(path);

        public bool CanWrite(string path) => true;

        public string? ResolveFinalPath(string path) => path;

        public bool? SameFilesystem(string first, string second) => true;
    }

    private const string Watched = "/media/movies/watched";
    private const string Work = "/media/movies/work";
    private const string Output = "/media/movies/output";

    private readonly StoreFixture _store = new();
    private readonly DataChangePublisher _changes = new();
    private readonly Probe _probe = new() { Existing = { Watched, Work, Output } };
    private readonly CapturingLogger<FolderReachability> _log = new();
    private readonly FolderReachability _reachability;

    public FolderReachabilityTests() =>
        _reachability = new FolderReachability(_store.Database, new LibraryStore(), _store.Options, _probe, _changes, _store.Clock, _log);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _reachability.StopAsync(CancellationToken.None);

    public void Dispose()
    {
        _reachability.Dispose();
        _store.Dispose();
    }

    private Task AddWorkflowAsync(string name = "Films", string watched = Watched, string work = Work, string output = Output, bool enabled = true) =>
        _store.Execute(
            "INSERT INTO libraries (name, media_type, enabled, watched_folder, work_folder, output_folder) " +
            $"VALUES ('{name}', 'movie', {(enabled ? 1 : 0)}, '{watched}', '{work}', '{output}')");

    private async Task<List<string>> TopicsAfterAsync(Func<Task> look)
    {
        using var heard = _changes.Subscribe();
        await look();
        heard.Dispose();
        var topics = new List<string>();
        await foreach (var topic in heard.ReadAllAsync(CancellationToken.None))
        {
            topics.Add(topic);
        }

        return topics;
    }

    [Fact]
    public async Task Folders_that_answer_leave_nothing_unreachable_and_say_nothing()
    {
        await AddWorkflowAsync();

        var topics = await TopicsAfterAsync(() => _reachability.LookAsync(CancellationToken.None));

        Assert.Empty(_reachability.Unreachable);
        Assert.Empty(topics);
    }

    [Fact]
    public async Task Each_folder_that_does_not_answer_is_named_in_plain_words_with_its_workflow()
    {
        await AddWorkflowAsync("Films");
        await AddWorkflowAsync("Series", watched: "/media/shows/watched", work: "/media/shows/work", output: "/media/shows/output");
        _probe.Existing.Remove(Watched);
        _probe.Existing.Remove("/media/shows/work");
        _probe.Unreadable.Add("/media/shows/output");
        _probe.Existing.UnionWith(["/media/shows/watched", "/media/shows/output"]);

        await _reachability.LookAsync(CancellationToken.None);

        Assert.Equal(
            ["The watched folder for Films", "The work folder for Series", "The output folder for Series"],
            _reachability.Unreachable);
    }

    [Fact]
    public async Task A_workflow_that_is_off_a_folder_not_set_and_a_default_work_folder_not_made_yet_are_not_unreachable()
    {
        _probe.Existing.Clear();
        await AddWorkflowAsync("Switched off one", enabled: false);
        await AddWorkflowAsync("Half set up", watched: string.Empty, work: string.Empty, output: string.Empty);
        await AddWorkflowAsync("Default work folder", watched: Watched, work: string.Empty, output: Output);
        _probe.Existing.UnionWith([Watched, Output]);

        await _reachability.LookAsync(CancellationToken.None);

        Assert.Empty(_reachability.Unreachable);
    }

    [Fact]
    public async Task A_change_of_answer_is_said_once_on_the_folder_checks_topic_and_the_same_answer_is_not_said_again()
    {
        await AddWorkflowAsync();
        _probe.Existing.Remove(Output);

        Assert.Equal([DataTopics.FolderChecks], await TopicsAfterAsync(() => _reachability.LookAsync(CancellationToken.None)));
        Assert.Empty(await TopicsAfterAsync(() => _reachability.LookAsync(CancellationToken.None)));

        _probe.Existing.Add(Output);
        Assert.Equal([DataTopics.FolderChecks], await TopicsAfterAsync(() => _reachability.LookAsync(CancellationToken.None)));
        Assert.Empty(_reachability.Unreachable);
    }

    [Fact]
    public async Task A_look_that_fails_keeps_the_last_answer_and_still_ends_the_wait_for_the_first_look()
    {
        await AddWorkflowAsync();
        _probe.Fails = true;

        await _reachability.LookAsync(CancellationToken.None);

        Assert.True(_reachability.FirstLook.IsCompletedSuccessfully);
        Assert.True(_log.Logged(LogLevel.Warning, "could not check whether it can reach"));
        Assert.Empty(_reachability.Unreachable);

        _probe.Fails = false;
        _probe.Existing.Remove(Watched);
        await _reachability.LookAsync(CancellationToken.None);
        _probe.Fails = true;
        await _reachability.LookAsync(CancellationToken.None);

        Assert.Equal(["The watched folder for Films"], _reachability.Unreachable);
    }

    [Fact]
    public async Task Running_it_looks_at_once_when_the_workflows_change_and_again_after_the_interval_whether_or_not_anyone_is_watching()
    {
        await _reachability.StartAsync(CancellationToken.None);
        await _reachability.FirstLook;
        Assert.Empty(_reachability.Unreachable);

        _probe.Existing.Remove(Watched);
        await AddWorkflowAsync();
        _changes.Publish(DataTopics.Libraries);
        await Eventually.ThatAsync(() => _reachability.Unreachable.SequenceEqual(["The watched folder for Films"]));

        _probe.Existing.Add(Watched);
        await Eventually.ThatAsync(() =>
        {
            _store.Clock.Advance(FolderReachability.Every);
            return _reachability.Unreachable.Count == 0;
        });
    }
}
