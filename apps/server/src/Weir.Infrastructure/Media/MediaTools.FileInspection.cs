namespace Weir.Infrastructure.Media;

/// <summary>The file facts <see cref="MediaTools.FfprobeJsonAsync"/> logs and checks: resolved path, existence, size and mtime (seconds since the epoch).</summary>
internal readonly record struct MediaFileState(string ResolvedPath, bool Exists, bool IsFile, long SizeBytes, double MtimeEpoch);

/// <summary>What a probe reads about a file before running ffprobe on it.</summary>
public sealed partial class MediaTools
{
    /// <summary>What <see cref="FfprobeJsonAsync"/> reads about the file before probing it.</summary>
    internal static MediaFileState InspectFile(string path)
    {
        var resolvedPath = ResolvePath(path);
        var exists = File.Exists(path) || Directory.Exists(path);
        var isFile = File.Exists(path);
        long size;
        double mtime;
        try
        {
            if (isFile)
            {
                var info = new FileInfo(path);
                size = info.Length;
                mtime = (info.LastWriteTimeUtc - DateTime.UnixEpoch).TotalSeconds;
            }
            else if (exists)
            {
                size = 0;
                mtime = (Directory.GetLastWriteTimeUtc(path) - DateTime.UnixEpoch).TotalSeconds;
            }
            else
            {
                size = -1;
                mtime = 0.0;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            size = -1;
            mtime = 0.0;
        }


        return new MediaFileState(resolvedPath, exists, isFile, size, mtime);
    }

    private static string ResolvePath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
