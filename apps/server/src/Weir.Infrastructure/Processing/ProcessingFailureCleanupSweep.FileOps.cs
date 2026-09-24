using Microsoft.Extensions.Logging;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Infrastructure.IO;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Processing;

/// <summary>The filesystem removal helpers shared by the Movies and TV halves of the Pass 4 failure-cleanup sweep.</summary>
public sealed partial class ProcessingFailureCleanupSweep
{
    /// <summary>The work-folder temp files Weir created for this source, matched by <see cref="WeirTempFiles.RemuxTempNameFor"/>.</summary>
    private static List<string> JobTempCandidates(string workRoot, string relNorm)
    {
        var result = new List<string>();
        if (relNorm.Length == 0 || !Directory.Exists(workRoot))
        {
            return result;
        }

        // Only the exact temp names Weir creates for this source (#534); an operator's own "Film.processing.notes.txt"
        // beside a failed "Film.mkv" is not Weir's to delete.
        var ownTempName = WeirTempFiles.RemuxTempNameFor(relNorm);
        List<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(workRoot)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var child in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            if (ownTempName.IsMatch(Path.GetFileName(child)))
            {
                result.Add(child);
            }
        }

        return result;
    }

    /// <summary>Removes now-empty ancestor folders, up to (not including) the root.</summary>
    private static void CascadeUnderRoot(string firstParent, string root, PyList deletedOut)
    {
        var current = RemuxPassPaths.Resolve(firstParent);
        var rr = RemuxPassPaths.Resolve(root);
        while (!RemuxPassPaths.SamePath(current, rr))
        {
            if (RemuxPassPaths.RelativeTo(current, rr) is null || !Directory.Exists(current))
            {
                break;
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                {
                    break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                break;
            }

            try
            {
                Directory.Delete(current);
                deletedOut.Items.Add(new PyStr(current));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                break;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }
    }

    /// <summary>A failed movie's source or output release, removed by <see cref="ReleaseFolderRemoval"/>; a locked folder is logged, not thrown.</summary>
    private ReleaseRemoval RemoveRelease(string root, string file, string? mediaExtensionsCsv)
    {
        try
        {
            return ReleaseFolderRemoval.Remove(root, file, mediaExtensionsCsv);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Failure cleanup could not remove the release of {File}; it is in use or blocked.", file);
            return new ReleaseRemoval(ReleaseRemovalKind.NothingRemoved, null);
        }
    }

    private (bool Ok, string? Error) SafeRmTree(string root, string path)
    {
        if (PathContainment.HasLinkBelowRoot(root, path))
        {
            _logger.LogWarning("Failure cleanup left {Path} alone: it, or a folder above it, is a link to another place.", path);
            return (false, ReleaseFolderRemoval.LinkedFolderReason);
        }

        try
        {
            Directory.Delete(path, recursive: true);
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var message = $"Could not remove {path} because it is in use or blocked ({exception.Message}).";
            _logger.LogWarning("Failure cleanup: {Message}", message);
            return (false, message);
        }
    }

    private (bool Ok, string? Error) SafeUnlink(string path)
    {
        try
        {
            File.Delete(path);
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            var message = $"Could not remove temp file {path} because it is in use or blocked ({exception.Message}).";
            _logger.LogWarning("Failure cleanup: {Message}", message);
            return (false, message);
        }
    }
}
