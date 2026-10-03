namespace Weir.Core.Updates;

/// <summary>Which of the published releases a running Weir should be offered.</summary>
public static class ReleaseSelection
{
    /// <summary>
    /// The newest release by SemVer precedence that is not a draft. Pre-releases are considered only when
    /// <paramref name="runningVersion"/> is itself a pre-release, so a stable install is never offered a release
    /// candidate. Null when nothing qualifies.
    /// </summary>
    public static GitHubReleaseRecord? NewestFor(IEnumerable<GitHubReleaseRecord> releases, string runningVersion)
    {
        ArgumentNullException.ThrowIfNull(releases);
        var includePreReleases = SemanticVersion.TryParse(runningVersion, out var running) && running.IsPreRelease;

        GitHubReleaseRecord? newest = null;
        SemanticVersion? newestVersion = null;
        foreach (var release in releases)
        {
            if (release.Draft || !SemanticVersion.TryParse(release.Version, out var version))
            {
                continue;
            }

            if ((release.Prerelease || version.IsPreRelease) && !includePreReleases)
            {
                continue;
            }

            if (newestVersion is null || version > newestVersion)
            {
                newest = release;
                newestVersion = version;
            }
        }

        return newest;
    }
}
