using System.Reflection;

namespace Weir.Core;

/// <summary>
/// The version the server reports: <c>WEIR_VERSION</c> when set,
/// otherwise the build's version, which MSBuild takes from <c>WeirVersion</c> in <c>apps/server/Directory.Build.props</c>.
/// </summary>
public static class WeirVersion
{
    public static string Resolve(string? versionOverride) =>
        string.IsNullOrWhiteSpace(versionOverride) ? BuildVersion : versionOverride.Trim();

    public static string BuildVersion { get; } = ReadBuildVersion();

    private static string ReadBuildVersion()
    {
        var informational = typeof(WeirVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        return WithoutBuildMetadata(informational);
    }

    /// <summary>
    /// Drops SemVer build metadata (<c>+abc123</c>) and keeps the rest, so <c>1.0.0-rc.1+abc123</c> reads as
    /// <c>1.0.0-rc.1</c>.
    /// </summary>
    public static string WithoutBuildMetadata(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
