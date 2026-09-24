using Weir.Core.Security;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>A database at head with the media manager services over it and a scripted manager for their HTTP.</summary>
internal sealed class MediaManagerFixture : IDisposable
{
    public const string CredentialsSecret = "credentials-secret";

    public MediaManagerFixture(params (string Name, string Value)[] variables)
    {
        Store = new StoreFixture([("WEIR_CREDENTIALS_SECRET", CredentialsSecret), .. variables]);
        Cipher = new CredentialCipher(Store.Options.CredentialsSecret, Store.Options.SessionSecret, Store.Options.PreviousCredentialsSecrets, Store.Clock);
        Http = new FakeManagerHttp();
        Ports = new HttpMediaManagerPorts(Http);
        ConnectionStore = new MediaManagerConnectionStore();
        Connections = new MediaManagerConnectionService(Store.Options, Cipher, Ports, ConnectionStore);
        Targets = new HandoffTargetStore();
        Files = new FileStateStore();
        Libraries = new LibraryStore();
        Handback = new HandbackStore();
        Ledger = new HandoffLedgerStore(Store.Clock, Targets, Files);
        Jobs = new ProcessingJobStore(Store.Database, Store.Clock);
        Intake = new MediaManagerIntake(Store.Options, Connections, ConnectionStore, Ledger, Targets, Jobs, Store.Clock);
        Reporter = new HandoffCompletionReporter(Connections, ConnectionStore, Ledger, Targets, Libraries, Http);
        OperatorSettings = new OperatorSettingsStore();
        Cancellation = new PendingJobCancellation(Ledger, Reporter, Files);
    }

    public StoreFixture Store { get; }

    public CredentialCipher Cipher { get; }

    public FakeManagerHttp Http { get; }

    public HttpMediaManagerPorts Ports { get; }

    public MediaManagerConnectionService Connections { get; }

    public MediaManagerConnectionStore ConnectionStore { get; }

    public HandoffTargetStore Targets { get; }

    public FileStateStore Files { get; }

    public LibraryStore Libraries { get; }

    public HandbackStore Handback { get; }

    public HandoffLedgerStore Ledger { get; }

    public ProcessingJobStore Jobs { get; }

    public MediaManagerIntake Intake { get; }

    public HandoffCompletionReporter Reporter { get; }

    public OperatorSettingsStore OperatorSettings { get; }

    public PendingJobCancellation Cancellation { get; }

    public Task<T> Db<T>(Func<UnitOfWork, Task<T>> work, bool commit = true) => Store.WithUnitOfWork(work, commit);

    public async Task<long> AddConnectionAsync(string kind, string name, string baseUrl = "http://manager.local", string? apiKey = "key", bool enabled = true) =>
        await Db(uow => Connections.CreateAsync(uow, kind, name, baseUrl, apiKey, enabled));

    /// <summary>Point the seeded library of <paramref name="mediaType"/> at a folder, or add one.</summary>
    public async Task<long> LibraryAsync(string mediaType, string watchedFolder, string? name = null)
    {
        if (name is null)
        {
            var existing = await Store.Scalar($"SELECT coalesce((SELECT id FROM libraries WHERE media_type = '{mediaType}' ORDER BY display_order, id LIMIT 1), 0)");
            if (existing != 0)
            {
                await Db(uow => uow.ExecuteAsync("UPDATE libraries SET watched_folder = $w WHERE id = $id", ("$w", watchedFolder), ("$id", existing)));
                return existing;
            }
        }

        return Convert.ToInt64(await Db(uow => uow.ExecuteScalarWriteAsync(
            "INSERT INTO libraries (name, media_type, watched_folder, display_order) VALUES ($n, $t, $w, 10) RETURNING id",
            ("$n", name ?? mediaType + " library"),
            ("$t", mediaType),
            ("$w", watchedFolder))), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose() => Store.Dispose();
}
