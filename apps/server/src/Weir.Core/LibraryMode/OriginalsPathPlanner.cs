namespace Weir.Core.LibraryMode;

/// <summary>
/// #735's "keep the original after a library clean": where a kept original goes. Pure path arithmetic only — the
/// actual move (or cross-volume copy) is <c>Weir.Infrastructure.LibraryMode.OriginalsMover</c>.
/// </summary>
/// <remarks>
/// Deliberately never uses <see cref="Path.Combine(string, string)"/> or <see cref="Path.GetRelativePath(string, string)"/>:
/// both assume the running OS's own separator, but a library folder can be typed with either style (a Docker container
/// reading a Windows share, for instance). Every join below reuses whichever separator the input path already has, the same
/// way <see cref="SafeSwapRules"/> builds its temp and backup names.
/// </remarks>
public static class OriginalsPathPlanner
{
    /// <summary>
    /// The default originals folder's name, inside whichever library folder held the file, used when a library has
    /// not set an explicit <see cref="LibrarySettings.OriginalsFolder"/>. Dot-prefixed so it reads as Weir's own
    /// folder, not part of the media collection.
    /// </summary>
    public const string DefaultFolderName = ".weir-originals";

    /// <summary>The library folder (one of <paramref name="libraryFolders"/>) that contains <paramref name="filePath"/>, or
    /// null when none of them do.</summary>
    public static string? ContainingFolder(IReadOnlyList<string> libraryFolders, string filePath)
    {
        ArgumentNullException.ThrowIfNull(libraryFolders);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        foreach (var folder in libraryFolders)
        {
            if (!string.IsNullOrWhiteSpace(folder) && IsWithin(folder, filePath))
            {
                return folder;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> itself, or something inside it.</summary>
    public static bool IsWithin(string folder, string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var trimmed = TrimTrailingSeparator(folder);
        if (string.Equals(path, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(trimmed + '\\', StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(trimmed + '/', StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where kept originals for files under <paramref name="libraryFolder"/> go: <paramref name="originalsFolder"/> when the
    /// library has set one, else the <see cref="DefaultFolderName"/> folder inside <paramref name="libraryFolder"/> itself.
    /// </summary>
    public static string DestinationFolder(string libraryFolder, string? originalsFolder)
    {
        if (!string.IsNullOrWhiteSpace(originalsFolder))
        {
            return originalsFolder;
        }

        ArgumentException.ThrowIfNullOrEmpty(libraryFolder);
        return $"{TrimTrailingSeparator(libraryFolder)}{SeparatorOf(libraryFolder)}{DefaultFolderName}";
    }

    /// <summary>
    /// Where a kept original of <paramref name="filePath"/> (a file under <paramref name="libraryFolder"/>) belongs, before
    /// any clash with a file already there is resolved: <see cref="DestinationFolder"/> plus the file's path relative to
    /// <paramref name="libraryFolder"/>, so files from different sub-folders never collide on name alone.
    /// </summary>
    public static string DestinationPath(string libraryFolder, string? originalsFolder, string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var folder = DestinationFolder(libraryFolder, originalsFolder);
        var relative = RelativePath(libraryFolder, filePath);
        return $"{TrimTrailingSeparator(folder)}{SeparatorOf(folder)}{relative}";
    }

    /// <summary>
    /// The first name at or after <paramref name="candidate"/> that <paramref name="exists"/> says is free, so a kept
    /// original never overwrites one already there: <c>Movie.mkv</c> becomes <c>Movie (2).mkv</c>, then <c>(3)</c>, up to
    /// <paramref name="limit"/> attempts.
    /// </summary>
    public static string AvoidCollision(string candidate, Func<string, bool> exists, int limit = 999)
    {
        ArgumentException.ThrowIfNullOrEmpty(candidate);
        ArgumentNullException.ThrowIfNull(exists);
        if (!exists(candidate))
        {
            return candidate;
        }

        var lastSeparator = candidate.LastIndexOfAny(['\\', '/']);
        var directory = lastSeparator < 0 ? string.Empty : candidate[..(lastSeparator + 1)];
        var name = candidate[(lastSeparator + 1)..];
        var dot = name.LastIndexOf('.');
        var stem = dot <= 0 ? name : name[..dot];
        var extension = dot <= 0 ? string.Empty : name[dot..];

        for (var attempt = 2; attempt <= limit; attempt++)
        {
            var next = $"{directory}{stem} ({attempt}){extension}";
            if (!exists(next))
            {
                return next;
            }
        }

        return $"{directory}{stem} ({limit}){extension}";
    }

    /// <summary><paramref name="path"/> with <paramref name="folder"/>'s prefix and its separator removed. Falls back to
    /// just the file name when <paramref name="path"/> is not actually under <paramref name="folder"/> (a defensive case:
    /// callers normally resolve <paramref name="folder"/> from <see cref="ContainingFolder"/> first).</summary>
    private static string RelativePath(string folder, string path)
    {
        var trimmed = TrimTrailingSeparator(folder);
        if (path.Length > trimmed.Length + 1 &&
            path.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) &&
            path[trimmed.Length] is '\\' or '/')
        {
            return path[(trimmed.Length + 1)..];
        }

        var lastSeparator = path.LastIndexOfAny(['\\', '/']);
        return lastSeparator < 0 ? path : path[(lastSeparator + 1)..];
    }

    private static string TrimTrailingSeparator(string path) => path.TrimEnd('\\', '/');

    /// <summary>The separator a path already uses, so a join never mixes styles: backslash only when the path has one and
    /// no forward slash at all, forward slash otherwise (POSIX default).</summary>
    private static char SeparatorOf(string path) => path.Contains('\\', StringComparison.Ordinal) && !path.Contains('/', StringComparison.Ordinal) ? '\\' : '/';
}
