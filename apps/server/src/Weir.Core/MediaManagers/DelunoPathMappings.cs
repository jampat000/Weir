namespace Weir.Core.MediaManagers;

/// <summary>
/// A folder as Deluno wrote it (<see cref="Original"/>) and as Weir sees it (<see cref="Path"/>). <see cref="Via"/> is the
/// mapping that translated it, or null when none covered it and the two are the same text.
/// </summary>
public sealed record MappedPath(string Original, string Path, DelunoPathMapping? Via)
{
    public bool WasMapped => Via is not null;
}

/// <summary>Turns a folder as Deluno sees it into the same folder as Weir sees it, through Deluno's processor path mappings.</summary>
public static class DelunoPathMappings
{
    /// <summary>
    /// Replaces the longest mapping <c>DelunoPath</c> that <paramref name="path"/> is inside with its <c>ProcessorPath</c>.
    /// Whole folders only, so <c>/data2</c> is not inside <c>/data</c>; case is ignored when either path is a Windows path
    /// and separators are normalised to the Weir side's. Of two mappings equally long the earlier wins, which is
    /// Deluno's higher priority.
    /// </summary>
    public static MappedPath Apply(IReadOnlyList<DelunoPathMapping> mappings, string path)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(path);
        var target = new ArrOsPath(path);
        DelunoPathMapping? longest = null;
        var longestFolders = -1;
        foreach (var mapping in mappings)
        {
            var source = new ArrOsPath(mapping.DelunoPath);
            var folders = source.Segments.Length;
            if (folders > longestFolders && source.Contains(target))
            {
                longest = mapping;
                longestFolders = folders;
            }
        }

        return longest is null
            ? new MappedPath(path, path, null)
            : new MappedPath(path, target.Remap(new ArrOsPath(longest.DelunoPath), new ArrOsPath(longest.ProcessorPath)).ToString(), longest);
    }
}
