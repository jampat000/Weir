namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Where a path really leads once every junction and symbolic link on the way is followed, so two names for one folder
/// (a mount reached as <c>C:\NasMount</c> and as <c>C:\Downloads</c>, say) compare as the one folder they are.
/// </summary>
public static class FinalPaths
{
    /// <summary>A loop of links, or an absurdly deep chain, gives up rather than spinning.</summary>
    private const int MaxLinksFollowed = 40;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// The path with every link followed, or null when that cannot be told from this machine: the path is not fully
    /// qualified, does not exist here, or a link on the way is broken or unreadable. It reads only link targets and never
    /// creates or changes anything.
    /// </summary>
    public static string? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var resolved = Path.GetPathRoot(full) ?? string.Empty;
            var pending = new Stack<string>(PartsBelowRoot(full).Reverse());
            var linksFollowed = 0;
            while (pending.Count > 0)
            {
                var candidate = Path.Join(resolved, pending.Pop());
                FileSystemInfo entry = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
                if (!entry.Exists)
                {
                    return null;
                }

                if (entry.LinkTarget is null)
                {
                    resolved = candidate;
                    continue;
                }

                if (++linksFollowed > MaxLinksFollowed || entry.ResolveLinkTarget(returnFinalTarget: false) is not { } target)
                {
                    return null;
                }

                resolved = Path.GetPathRoot(target.FullName) ?? string.Empty;
                foreach (var part in PartsBelowRoot(target.FullName).Reverse())
                {
                    pending.Push(part);
                }
            }

            return resolved;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string[] PartsBelowRoot(string fullPath) =>
        fullPath[(Path.GetPathRoot(fullPath)?.Length ?? 0)..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
}
