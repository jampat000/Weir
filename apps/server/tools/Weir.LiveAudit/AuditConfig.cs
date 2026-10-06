using System.Text.RegularExpressions;

namespace Weir.LiveAudit;

/// <summary>Environment-derived configuration, read once, so every part of the audit sees the same values.</summary>
internal sealed partial class AuditConfig
{
    public const int TimeoutMs = 30_000;

    public string BaseUrl { get; } = Read("WEIR_LIVE_BASE_URL", "").TrimEnd('/');
    public string AuditUser { get; } = Read("WEIR_LIVE_E2E_USER", "live-audit-admin");
    public string AuditPassword { get; } = Environment.GetEnvironmentVariable("WEIR_LIVE_E2E_PASSWORD")
        ?? "live-audit-pass-20260831";
    public string ArtifactDir { get; } = Environment.GetEnvironmentVariable("WEIR_LIVE_E2E_ARTIFACTS")
        ?? Path.Combine("artifacts", "live-packaged-e2e");
    public string FixtureHostRoot { get; } = Read("WEIR_LIVE_E2E_FIXTURE_HOST_ROOT", "");
    public string FixtureServerRoot { get; } = Read("WEIR_LIVE_E2E_FIXTURE_SERVER_ROOT", "");
    public string FixtureFfmpeg { get; } = Read("WEIR_LIVE_E2E_FFMPEG", "ffmpeg");

    /// <summary>
    /// The candidate's peer, seen from inside a Docker container, is the bridge gateway rather than loopback, so
    /// bootstrap needs the one-time setup code the same way a remote operator would get it. Empty for a target the
    /// audit reaches over real loopback (a developer server, a Windows smoke).
    /// </summary>
    public string DockerContainer { get; } = Read("WEIR_LIVE_E2E_DOCKER_CONTAINER", "");
    public string DockerWeirHome { get; } = Read("WEIR_LIVE_E2E_DOCKER_HOME", "/data/weir");

    public string ExpectedVersion => expectedVersion.Value;

    private readonly Lazy<string> expectedVersion = new(ResolveExpectedVersion);

    private static string Read(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return value is null ? fallback : value.Trim();
    }

    /// <summary>The expected packaged version: the explicit variable, else the version the repository declares.</summary>
    private static string ResolveExpectedVersion()
    {
        var explicitVersion = Read("WEIR_LIVE_EXPECTED_VERSION", "");
        if (explicitVersion.Length > 0)
        {
            return explicitVersion;
        }

        var props = FindBuildProps()
            ?? throw new InvalidOperationException(
                "WEIR_LIVE_EXPECTED_VERSION is not set and apps/server/Directory.Build.props was not found.");
        var match = WeirVersionPattern().Match(File.ReadAllText(props));
        if (!match.Success)
        {
            throw new InvalidOperationException($"WeirVersion was not found in {props}");
        }

        return match.Groups[1].Value.Trim();
    }

    private static string? FindBuildProps()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "apps", "server", "Directory.Build.props");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    [GeneratedRegex("<WeirVersion>([^<]+)</WeirVersion>")]
    private static partial Regex WeirVersionPattern();
}
