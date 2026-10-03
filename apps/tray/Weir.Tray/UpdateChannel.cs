using System.Reflection;
using Velopack.Sources;

namespace Weir.Tray;

/// <summary>Which GitHub releases this install updates to: stable releases only, unless it is itself a pre-release.</summary>
static class UpdateChannel
{
    /// <summary>
    /// The release source for an install running <paramref name="runningVersion"/>. Velopack's GitHub source skips
    /// pre-releases unless asked, so an install on <c>1.0.0-rc.1</c> would never see <c>1.0.0-rc.2</c>.
    /// </summary>
    internal static GithubSource SourceFor(string repoUrl, string? runningVersion) =>
        new(repoUrl, accessToken: null, prerelease: IncludesPreReleases(runningVersion));

    internal static GithubSource SourceForThisBuild(string repoUrl) =>
        SourceFor(repoUrl, typeof(UpdateChannel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Whether an install running <paramref name="runningVersion"/> is offered pre-releases. A pre-release has a hyphen in its
    /// version (<c>1.0.0-rc.1</c>); the numbers before it never do, and build metadata (<c>+abc123</c>) is not part of the check.
    /// </summary>
    internal static bool IncludesPreReleases(string? runningVersion)
    {
        var withoutMetadata = (runningVersion ?? string.Empty).Split('+')[0];
        return withoutMetadata.Contains('-', StringComparison.Ordinal);
    }
}
