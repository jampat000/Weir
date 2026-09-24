namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// #735: moves a library swap's backup into its kept-original destination instead of deleting it, per
/// docs/file-lifecycle-contract.md's cross-volume rule — a same-volume rename, or (when <see cref="ISwapFileSystem.Move"/>
/// refuses to cross volumes) a copy verified by size before the source is deleted. Shared by <see cref="SafeSwap"/>'s
/// commit step and <see cref="SwapRecoverySweep"/>'s recovery of a crash that interrupted it.
/// </summary>
public static class OriginalsMover
{
    /// <summary>Moves <paramref name="source"/> to <paramref name="destination"/>, creating the destination's folder first.
    /// Never overwrites an existing destination — the caller has already chosen a free name.</summary>
    public static void MoveOrCopy(ISwapFileSystem files, string source, string destination)
    {
        ArgumentNullException.ThrowIfNull(files);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            files.EnsureDirectory(directory);
        }

        try
        {
            files.Move(source, destination);
            return;
        }
        catch (CrossVolumeException)
        {
        }

        var sourceSize = files.FileSizeBytes(source);
        files.Copy(source, destination);
        var destinationSize = files.FileSizeBytes(destination);
        if (destinationSize != sourceSize)
        {
            files.Delete(destination);
            throw new IOException(
                $"The copy to '{destination}' was {destinationSize} bytes; the original is {sourceSize}. The original was left in place.");
        }

        files.Delete(source);
    }

    /// <summary>
    /// Finishes a keep the recovery sweep found interrupted: when <paramref name="destination"/> already exists (a crash
    /// between the copy and deleting <paramref name="source"/>) and its size matches, only the leftover backup is removed.
    /// Otherwise the move or copy runs as it would the first time. Never deletes <paramref name="source"/> without first
    /// confirming <paramref name="destination"/> is intact; a size mismatch throws rather than guessing, so the caller
    /// counts it as a problem and leaves both files for a person to look at.
    /// </summary>
    public static void Recover(ISwapFileSystem files, string source, string destination)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.FileExists(destination))
        {
            if (files.FileSizeBytes(destination) != files.FileSizeBytes(source))
            {
                throw new IOException(
                    $"The kept original at '{destination}' does not match the size of its backup '{source}'; neither was touched.");
            }

            files.Delete(source);
            return;
        }

        MoveOrCopy(files, source, destination);
    }
}
