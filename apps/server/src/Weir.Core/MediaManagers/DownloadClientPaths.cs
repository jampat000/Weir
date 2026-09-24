namespace Weir.Core.MediaManagers;

/// <summary>
/// Minimal path joining for a download client's own folder layout. This is the client's OS, not the one Weir
/// runs on, so <see cref="System.IO.Path"/> (which follows the running OS's rules) cannot be used.
/// </summary>
public static class DownloadClientPaths
{
    /// <summary>A Unix root, a Windows drive letter, or a UNC root.</summary>
    public static bool IsAbsolute(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (path[0] is '/' or '\\')
        {
            return true;
        }

        return path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]);
    }

    /// <summary>Joins a relative path onto a base folder, using whichever separator the base folder already uses.</summary>
    public static string Join(string basePath, string relative)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(relative);
        var separator = basePath.Contains('\\') && !basePath.Contains('/') ? '\\' : '/';
        var trimmedBase = basePath.TrimEnd('/', '\\');
        var trimmedRelative = relative.TrimStart('/', '\\');
        return $"{trimmedBase}{separator}{trimmedRelative}";
    }
}
