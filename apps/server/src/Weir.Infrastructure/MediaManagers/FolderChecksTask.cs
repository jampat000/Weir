using Weir.Core.Json;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Scheduling;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// <c>folder-checks</c>: while a browser is watching, checks each switched-on workflow's folder chain
/// (<see cref="LibraryFolderChainCheck"/>) on its own and says so on <see cref="DataTopics.FolderChecks"/> when any answer is not
/// the one it gave last time, so a folder that goes missing, or comes back, turns Health red or green without anyone asking.
/// The first answer for a workflow counts as a change, because a page that opened a moment earlier may have read an older one.
/// Each look is told to the open streams as the time Health shows (<see cref="ServerLooks"/>). Idle, with no stream open, it does not
/// touch the disk, the database or a media manager.
/// </summary>
public sealed class FolderChecksTask : IPeriodicTask
{
    private readonly SqliteDatabase _database;
    private readonly LibraryStore _libraries;
    private readonly LibraryFolderChainCheck _check;
    private readonly ActivityStreamClients _clients;
    private readonly DataChangePublisher _changes;
    private readonly ServerLooks _looks;
    private readonly Dictionary<long, string> _answers = [];

    public FolderChecksTask(
        SqliteDatabase database, LibraryStore libraries, LibraryFolderChainCheck check, ActivityStreamClients clients, DataChangePublisher changes,
        ServerLooks looks)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _libraries = libraries ?? throw new ArgumentNullException(nameof(libraries));
        _check = check ?? throw new ArgumentNullException(nameof(check));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _changes = changes ?? throw new ArgumentNullException(nameof(changes));
        _looks = looks ?? throw new ArgumentNullException(nameof(looks));
    }

    public string Name => "folder-checks";

    public string? Label => null;

    public TimeSpan Interval => TimeSpan.FromSeconds(15);

    public bool RunAtStart => false;

    public TimeSpan? FailureCooldown => null;

    public string FailureMessage => "Weir could not check its workflows' folders.";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_clients.Count == 0)
        {
            return;
        }

        var changed = false;
        var uow = await UnitOfWork.OpenAsync(_database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var enabled = await _libraries.ListAsync(uow, enabledOnly: true).ConfigureAwait(false);
            foreach (var library in enabled)
            {
                var chain = await _check.CheckForLibraryAsync(uow, library, cancellationToken).ConfigureAwait(false);
                var answer = WireJsonWriter.Dumps(chain, WireJsonFormat.Compact);
                if (!_answers.TryGetValue(library.Id, out var before) || before != answer)
                {
                    _answers[library.Id] = answer;
                    changed = true;
                }
            }

            foreach (var departed in _answers.Keys.Except(enabled.Select(library => library.Id)).ToList())
            {
                _answers.Remove(departed);
            }
        }

        _looks.FoldersLooked();
        if (changed)
        {
            _changes.Publish(DataTopics.FolderChecks);
        }
    }
}
