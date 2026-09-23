namespace Weir.Tray;

/// <summary>
/// What the bundled server is started with. The tray sets these in its own environment, and every server process it
/// starts inherits them.
/// </summary>
static class ServerEnvironment
{
    internal const string SessionSecretFileName = "session.secret";
    internal const string CredentialsSecretFileName = "credentials.secret";
    internal const string CredentialsSecretVariable = "WEIR_CREDENTIALS_SECRET";

    private const string WebDistFolderName = "web-dist";
    private const string WebEntryFileName = "index.html";
    private const string EnvironmentVariable = "WEIR_ENV";
    private const string ProductionEnvironment = "production";
    private const string WebDistVariable = "WEIR_WEB_DIST";
    private const string SessionSecretVariable = "WEIR_SESSION_SECRET";

    // The desktop app serves plain HTTP on localhost, where a Secure cookie would never be sent back.
    private const string CookieSecureVariable = "WEIR_SESSION_COOKIE_SECURE";

    /// <summary>
    /// Production mode, this runtime home, the web app bundled next to the server in
    /// <paramref name="serverDirectory"/>, and the two secrets.
    /// </summary>
    internal static void Apply(string runtimeHome, string serverDirectory)
    {
        var webDist = Path.Combine(serverDirectory, WebDistFolderName);
        if (!File.Exists(Path.Combine(webDist, WebEntryFileName)))
        {
            throw new InvalidOperationException("Bundled web assets are missing from the Weir desktop package.");
        }

        Environment.SetEnvironmentVariable(EnvironmentVariable, ProductionEnvironment);
        Environment.SetEnvironmentVariable(Program.RuntimeHomeVariable, runtimeHome);
        Environment.SetEnvironmentVariable(WebDistVariable, webDist);
        Environment.SetEnvironmentVariable(CookieSecureVariable, bool.FalseString.ToLowerInvariant());
        Environment.SetEnvironmentVariable(SessionSecretVariable, SecretFile.Ensure(runtimeHome, SessionSecretFileName));
        Environment.SetEnvironmentVariable(CredentialsSecretVariable, CredentialsSecret(runtimeHome, Environment.GetEnvironmentVariable));
    }

    /// <summary>
    /// The secret stored API keys are encrypted with: the operator's own WEIR_CREDENTIALS_SECRET if they set one,
    /// because keys stored under it only decrypt with it; otherwise credentials.secret in the runtime home. Keys
    /// saved before the server had a credentials secret keep decrypting: it reads those with the session secret,
    /// which never changes (CredentialCipher, the "session-legacy:v1" key id).
    /// </summary>
    internal static string CredentialsSecret(string runtimeHome, Func<string, string?> getEnvironment)
    {
        var configured = getEnvironment(CredentialsSecretVariable)?.Trim();
        return string.IsNullOrEmpty(configured)
            ? SecretFile.Ensure(runtimeHome, CredentialsSecretFileName)
            : configured;
    }
}
