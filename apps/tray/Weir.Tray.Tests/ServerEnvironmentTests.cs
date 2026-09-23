using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// Stored API keys get their own secret, separate from the session secret. Keys saved before that keep
/// decrypting because the server reads them with the session secret, which the tray never replaces; the server's
/// own tests pin that side (PythonSecurityCompatibilityTests in apps/server/tests/Weir.Core.Tests).
/// </summary>
public sealed class ServerEnvironmentTests : IDisposable
{
    private readonly TempDirectory _home = TempDirectory.AsWeirHome();

    public void Dispose() => _home.Dispose();

    private static string? NotSet(string name) => null;

    [Fact]
    public void Without_one_configured_the_credentials_secret_is_kept_in_the_runtime_home()
    {
        var secret = ServerEnvironment.CredentialsSecret(_home.Path, NotSet);

        Assert.Equal(secret, File.ReadAllText(Path.Combine(_home.Path, ServerEnvironment.CredentialsSecretFileName)));
    }

    [Fact]
    public void The_credentials_secret_is_the_same_on_every_start()
    {
        var first = ServerEnvironment.CredentialsSecret(_home.Path, NotSet);

        var second = ServerEnvironment.CredentialsSecret(_home.Path, NotSet);

        Assert.Equal(first, second);
    }

    [Fact]
    public void An_operators_own_credentials_secret_is_used_and_no_file_is_written()
    {
        var own = new string('c', 40);

        var secret = ServerEnvironment.CredentialsSecret(
            _home.Path, name => name == ServerEnvironment.CredentialsSecretVariable ? $"  {own}  " : null);

        Assert.Equal(own, secret);
        Assert.False(File.Exists(Path.Combine(_home.Path, ServerEnvironment.CredentialsSecretFileName)));
    }

    [Fact]
    public void Adding_the_credentials_secret_leaves_the_session_secret_alone()
    {
        var sessionPath = Path.Combine(_home.Path, ServerEnvironment.SessionSecretFileName);
        var session = new string('s', 40);
        File.WriteAllText(sessionPath, session);

        var credentials = ServerEnvironment.CredentialsSecret(_home.Path, NotSet);

        Assert.Equal(session, File.ReadAllText(sessionPath));
        Assert.NotEqual(session, credentials);
    }
}
