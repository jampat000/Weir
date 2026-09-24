using Weir.Core.Security;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>A database at head with the download client services over it and a scripted client for their HTTP (#768).</summary>
internal sealed class DownloadClientFixture : IDisposable
{
    public const string CredentialsSecret = "download-client-credentials-secret";

    public DownloadClientFixture(params (string Name, string Value)[] variables)
    {
        Store = new StoreFixture([("WEIR_CREDENTIALS_SECRET", CredentialsSecret), .. variables]);
        Cipher = new CredentialCipher(Store.Options.CredentialsSecret, Store.Options.SessionSecret, Store.Options.PreviousCredentialsSecrets, Store.Clock);
        Http = new FakeManagerHttp();
        Ports = new DownloadClientPorts([new SabnzbdPort(Http), new NzbgetPort(Http), new QBittorrentPort(Http), new DelugePort(Http), new TransmissionPort(Http)]);
        Connections = new DownloadClientConnectionService(Cipher);
        Suggestions = new DownloadClientSuggestions(Connections, Ports);
    }

    public StoreFixture Store { get; }

    public CredentialCipher Cipher { get; }

    public FakeManagerHttp Http { get; }

    public DownloadClientPorts Ports { get; }

    public DownloadClientConnectionService Connections { get; }

    public DownloadClientSuggestions Suggestions { get; }

    public Task<T> Db<T>(Func<UnitOfWork, Task<T>> work, bool commit = true) => Store.WithUnitOfWork(work, commit);

    public Task<long> AddConnectionAsync(
        string kind, string name, string baseUrl = "http://client.local", string? username = null, string? password = null, string? apiKey = null, bool enabled = true) =>
        Db(uow => Connections.CreateAsync(uow, kind, name, baseUrl, username, password, apiKey, enabled));

    public void Dispose() => Store.Dispose();
}
