using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>Placing an output under the manager's own path space, for a library that refines before import.</summary>
public sealed partial class HandoffCompletionReporter
{
    /// <summary>
    /// <paramref name="outputFile"/> rebuilt under the manager's output folder, or null when it is
    /// not under ours.
    /// </summary>
    public static string? TranslateOutputPath(string outputFile, string localOutputFolder, string managerOutputFolder)
    {
        string file;
        string folder;
        try
        {
            file = Path.GetFullPath(outputFile);
            folder = Path.GetFullPath(localOutputFolder);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fileParts = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p.Length > 0).ToList();
        var folderParts = folder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Where(p => p.Length > 0).ToList();
        if (fileParts.Count <= folderParts.Count || !folderParts.Select((part, i) => string.Equals(part, fileParts[i], comparison)).All(same => same))
        {
            return null;
        }

        return CompletionReports.ManagerPathJoin(managerOutputFolder, fileParts.Skip(folderParts.Count));
    }

    /// <summary>The output as the manager will see it, or null to fall back to the local path.</summary>
    private async Task<string?> ManagerOutputPathAsync(ManagerConnection connection, HandoffOrigin origin, WireObject result, CancellationToken cancellationToken)
    {
        if (result.Get("output_file") is not WireString outputFile || result.Get("processing_output_folder_resolved") is not WireString localFolder)
        {
            return null;
        }

        return await ManagerOutputFolderAsync(connection, origin, cancellationToken).ConfigureAwait(false) is { } managerFolder
            ? TranslateOutputPath(outputFile.Value, localFolder.Value, managerFolder)
            : null;
    }

    /// <summary>
    /// The output folder the manager's own library uses for this hand-off, as the manager sees it, when that library
    /// processes before import; null when there is none or the manager cannot say.
    /// </summary>
    private async Task<string?> ManagerOutputFolderAsync(ManagerConnection connection, HandoffOrigin origin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(origin.LibraryId) || _connections.Ports.PortForKind(connection.Kind) is not { } port)
        {
            return null;
        }

        ManagerDescription description;
        try
        {
            description = await port.DescribeAsync(connection, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An unreadable library list only means the output path cannot be placed; the report still goes out.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Could not read {Label}'s libraries to place the output path.", connection.Label);
            return null;
        }

        return description.Libraries
            .FirstOrDefault(library => library.Key == origin.LibraryId && library.ProcessesBeforeImport && !string.IsNullOrEmpty(library.OutputPath))
            ?.OutputPath;
    }
}
