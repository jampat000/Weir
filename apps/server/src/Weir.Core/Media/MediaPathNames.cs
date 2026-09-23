namespace Weir.Core.Media;

/// <summary>A path's name, suffix and stem, where a dot file such as <c>.hidden</c> has no suffix (unlike <see cref="Path"/>).</summary>
public static class MediaPathNames
{
    /// <summary>The final component.</summary>
    public static string Name(string path, bool windows)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = MediaToolLocations.Normalize(path, windows);
        var cut = windows ? normalized.LastIndexOfAny(['\\', ':']) : normalized.LastIndexOf('/');
        var name = normalized[(cut + 1)..];
        return name == "." ? string.Empty : name;
    }

    /// <summary>The final component's last dot-suffix: empty for <c>.bashrc</c> and for a name ending in a dot.</summary>
    public static string Suffix(string path, bool windows)
    {
        var name = Name(path, windows);
        var i = name.LastIndexOf('.');
        return i > 0 && i < name.Length - 1 ? name[i..] : string.Empty;
    }

    /// <summary>The final component without its suffix.</summary>
    public static string Stem(string path, bool windows)
    {
        var name = Name(path, windows);
        var i = name.LastIndexOf('.');
        return i > 0 && i < name.Length - 1 ? name[..i] : name;
    }
}
