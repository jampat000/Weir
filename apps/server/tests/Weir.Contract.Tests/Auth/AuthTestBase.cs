using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Auth;

/// <summary>A test class on one shared server; sessions it opens are disposed with each test.</summary>
public abstract class AuthTestBase(ServerFixture fixture) : IAsyncLifetime
{
    private readonly List<WeirClient> _sessions = [];

    protected WeirServer Server => fixture.Server;

    /// <summary>A new anonymous session (own cookie jar) on the server's current address.</summary>
    protected WeirClient NewSession(IReadOnlyDictionary<string, string>? headers = null) => Track(Server.CreateClient(headers));

    /// <summary>A new anonymous session on a server of the test's own.</summary>
    protected WeirClient NewSession(WeirServer server, IReadOnlyDictionary<string, string>? headers = null) => Track(server.CreateClient(headers));

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual Task DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }

        return Task.CompletedTask;
    }

    private WeirClient Track(WeirClient session)
    {
        _sessions.Add(session);
        return session;
    }
}

/// <summary>An <see cref="AuthTestBase"/> where the admin <c>alice</c> exists and <see cref="Alice"/> is an anonymous session on the shared server.</summary>
public abstract class AliceTestBase(ServerFixture fixture) : AuthTestBase(fixture)
{
    protected WeirClient Alice { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        Alice = NewSession();
        await Alice.EnsureAdminAccountAsync();
    }
}
