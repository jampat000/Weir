using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>Either the library-relative paths a hand-off's file list means, or the reason it cannot be used.</summary>
public sealed record HandoffSourceFilesResult(IReadOnlyList<string>? RelativePaths, string? Problem)
{
    public bool Ok => RelativePaths is not null;
}

/// <summary>
/// A hand-off's <c>sourceFiles</c>: the files a manager means inside the folder it names, so the other files in that folder are
/// never looked at. Each path is settled textually, like <see cref="HandoffPaths"/>, since the filesystem is not touched here.
/// </summary>
public static class HandoffSourceFiles
{
    /// <summary>
    /// The library-relative path of each listed file, given the hand-off's own path (<paramref name="sourcePath"/>) and where that
    /// path sits in its library (<paramref name="sourceRelativePath"/>). A file that is not at or below the folder the hand-off names
    /// refuses the whole list, whether it says so by another folder or by climbing out with <c>..</c>. A file listed twice counts once.
    /// </summary>
    public static HandoffSourceFilesResult Resolve(string sourcePath, string sourceRelativePath, IReadOnlyList<string> listed, bool windows)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);
        ArgumentNullException.ThrowIfNull(sourceRelativePath);
        ArgumentNullException.ThrowIfNull(listed);
        var comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var folder = Parts(sourcePath);
        var relatives = new List<string>(listed.Count);
        foreach (var path in listed)
        {
            if (WireStrings.Strip(path).Length == 0)
            {
                return new HandoffSourceFilesResult(null, NoPathDetail);
            }

            var file = Parts(path);
            if (folder is null || file is null || file.Count < folder.Count || !file.Take(folder.Count).SequenceEqual(folder, comparer))
            {
                return new HandoffSourceFilesResult(null, OutsideDetail(path));
            }

            var relative = string.Join('/', new[] { sourceRelativePath }.Concat(file.Skip(folder.Count)));
            if (!relatives.Contains(relative, comparer))
            {
                relatives.Add(relative);
            }
        }

        return new HandoffSourceFilesResult(relatives, null);
    }

    /// <summary>A path's parts with <c>.</c> and <c>..</c> settled, or null when it climbs out above its own start.</summary>
    private static List<string>? Parts(string path)
    {
        var parts = new List<string>();
        foreach (var part in WireStrings.Strip(path).Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part != "..")
            {
                parts.Add(part);
            }
            else if (parts.Count == 0)
            {
                return null;
            }
            else
            {
                parts.RemoveAt(parts.Count - 1);
            }
        }

        return parts;
    }

    public const string NoPathDetail = "The hand-off lists a file with no path. Nothing was queued.";

    public const string NoVideoDetail = "None of the files the hand-off lists is a video file Weir processes. Nothing was queued.";

    /// <summary>Names only the file, as <see cref="HandoffPaths"/> does, so a refusal never discloses the layout of Weir's own disk.</summary>
    public static string OutsideDetail(string path) =>
        $"The hand-off lists {WireStrings.Repr(NameOf(path))}, which is not inside the folder the hand-off names. Nothing was queued.";

    public static string MissingDetail(string path) =>
        $"The hand-off lists {WireStrings.Repr(NameOf(path))}, but Weir cannot find that file. " +
        "Point the media manager and Weir at the same folder — both hosts have to see it at that path. Nothing was queued.";

    private static string NameOf(string path) =>
        WireStrings.Strip(path).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
}
