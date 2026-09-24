using Weir.Core.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// The real, disk-touching <see cref="IFolderProbe"/>. Existence is a plain <see cref="Directory.Exists"/> — a folder
/// chain check never creates one as a side effect, unlike <c>WatchedFolderScanOps.OutputFolderProblem</c>, which is
/// called only once a pass is actually about to write there. Reading is a directory listing; writing is a probe file,
/// created and removed through the same <see cref="FileLifecycle.BestEffortDelete"/> cleanup the remux pass uses for its
/// own temp files. Same-filesystem reuses <see cref="FilesystemBoundaries.SameFilesystem"/> (#716) rather than
/// re-deriving a drive comparison here.
/// </summary>
public sealed class FilesystemFolderProbe : IFolderProbe
{
    public bool Exists(string path) => Directory.Exists(path);

    public bool CanRead(string path)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            entries.MoveNext();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool CanWrite(string path)
    {
        var probe = Path.Combine(path, $".weir-folder-chain-test-{Environment.ProcessId}");
        try
        {
            File.WriteAllBytes(probe, "0"u8.ToArray());
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            FileLifecycle.BestEffortDelete(probe);
        }
    }

    public bool? SameFilesystem(string first, string second) => FilesystemBoundaries.SameFilesystem(first, second);
}
