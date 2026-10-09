namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// A file that was deleted, or whose folder was deleted, while Weir had work queued for it. That is an ordinary thing to do to a
/// download, so every route that works on a source file settles it as nothing to do instead of as a failure.
/// </summary>
internal static class GoneSources
{
    /// <summary>
    /// True when the watched folder is there and the file under it is not. A watched folder that is itself missing is a real problem
    /// (an unmounted share, a deleted workflow folder), not a file that went away, so it is never reported as gone.
    /// </summary>
    public static bool HasLeft(string watchedFolder, string relativePath)
    {
        try
        {
            var source = RemuxPassPaths.ResolveMediaFileUnderRoot(watchedFolder, relativePath);
            return !File.Exists(source) && !Directory.Exists(source);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
