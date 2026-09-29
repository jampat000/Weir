namespace Weir.Core.MediaManagers;

/// <summary>
/// The fix text for a folder Deluno reports that is not the one Weir uses: either Deluno maps its path to Weir's, or Weir
/// takes Deluno's path. <see cref="For"/> is for a Deluno that does not publish its path mappings, where Weir cannot tell a
/// mapped setup from a mistaken one and names both ways out; the other members are for one that does.
/// </summary>
internal static class DelunoPathMappingAdvice
{
    /// <summary>Where Deluno's path mappings are edited.</summary>
    public const string MappingsMenu = "Settings › Media Management › Processing Workflow › Weir › Path mappings";

    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// "If Deluno has a path mapping from A to B (…), this is fine. Otherwise set the watched folder to C." A is the
    /// part of Deluno's path that differs from Weir's and B the part of Weir's; when the two share no trailing folders,
    /// they are the whole paths.
    /// </summary>
    public static string For(string managerLabel, string delunoPath, string weirPath, string weirFolderRole)
    {
        var (from, to) = DifferingPrefixes(delunoPath, weirPath) ?? (delunoPath, weirPath);
        return $"If {managerLabel} has a path mapping from {from} to {to} ({MappingsMenu}), this is fine. Otherwise set {weirFolderRole} to {delunoPath}.";
    }

    /// <summary>
    /// The fix for a folder that Deluno's own mapping translates, but not into the folder Weir uses: use the folder
    /// Weir already receives, or correct the Weir side of the mapping.
    /// </summary>
    public static string WhenMappedElsewhere(string managerLabel, MappedPath folder, string weirFolderRole) =>
        $"Set {weirFolderRole} to {folder.Path}, or correct the Weir folder of that path mapping in {managerLabel} ({MappingsMenu}).";

    /// <summary>
    /// The fix for a folder none of Deluno's mappings covers, naming the mappings it does have (or that it has none):
    /// add the mapping, or use Deluno's folder.
    /// </summary>
    public static string WhenNoMappingCovers(
        string managerLabel, IReadOnlyList<DelunoPathMapping> mappings, string delunoPath, string weirPath, string weirFolderRole)
    {
        var (from, to) = DifferingPrefixes(delunoPath, weirPath) ?? (delunoPath, weirPath);
        var existing = mappings.Count == 0
            ? $"{managerLabel} has no path mappings for Weir."
            : $"None of {managerLabel}'s path mappings for Weir covers it ({string.Join("; ", mappings.Select(mapping => $"{mapping.DelunoPath} to {mapping.ProcessorPath}"))}).";
        return $"{existing} Add a path mapping from {from} to {to} in {managerLabel} ({MappingsMenu}), or set {weirFolderRole} to {delunoPath}.";
    }

    /// <summary>
    /// The two paths with their shared trailing folders taken off (<c>C:\NasMount</c> and <c>C:\Downloads</c> for
    /// <c>C:\NasMount\Completed\Movies</c> and <c>C:\Downloads\Completed\Movies</c>), or null when they share no whole
    /// trailing folder or nothing would be left of either.
    /// </summary>
    private static (string Deluno, string Weir)? DifferingPrefixes(string delunoPath, string weirPath)
    {
        var deluno = delunoPath.TrimEnd(Separators);
        var weir = weirPath.TrimEnd(Separators);
        var shared = 0;
        while (shared < deluno.Length && shared < weir.Length && SameCharacter(deluno[^(shared + 1)], weir[^(shared + 1)]))
        {
            shared++;
        }

        while (shared > 0 && !(StartsFolder(deluno, shared) && StartsFolder(weir, shared)))
        {
            shared--;
        }

        return shared == 0
            ? null
            : (deluno[..(deluno.Length - shared - 1)], weir[..(weir.Length - shared - 1)]);
    }

    /// <summary>Whether the last <paramref name="tailLength"/> characters are whole folders, with something before them.</summary>
    private static bool StartsFolder(string path, int tailLength)
    {
        var start = path.Length - tailLength;
        return start >= 2 && IsSeparator(path[start - 1]) && !IsSeparator(path[start]);
    }

    private static bool SameCharacter(char first, char second) =>
        (IsSeparator(first) && IsSeparator(second)) || char.ToLowerInvariant(first) == char.ToLowerInvariant(second);

    private static bool IsSeparator(char character) => character is '/' or '\\';
}
