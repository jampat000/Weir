using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Settings;

/// <summary>Restoring <c>libraries</c> and <c>rule_sets</c> from a bundle, and the validation that runs before any row is written.</summary>
public static partial class ConfigurationBundleStore
{
    /// <summary>
    /// Replaces <c>libraries</c> and <c>rule_sets</c> from the bundle's own rows. When the bundle carries its media
    /// managers, a library found through one points at that connection's id on this install.
    /// </summary>
    /// <remarks>
    /// Rows are inserted as they are, with no renaming (such as <c>media_scope</c> to <c>media_type</c>, #557):
    /// a version 4 bundle always carries both sections in the current shape. Every library row is validated
    /// before any row is written (see <see cref="ValidateRestoredLibraries"/>), the same checks
    /// <c>POST /processing/libraries</c> runs: a bad row refuses the whole restore rather than leaving the
    /// table partly replaced.
    /// </remarks>
    private static async Task RestoreProcessingLibrariesAsync(
        UnitOfWork uow, PyDict bundle, string weirHome, IReadOnlyDictionary<long, long>? restoredConnectionIds)
    {
        if (!bundle.ContainsKey(LibrariesTable))
        {
            return;
        }

        var libraryRows = Iterate(bundle[LibrariesTable])
            .Select(row => row as PyDict ?? throw new PyTypeErrorException($"Each row in the backup's {LibrariesTable} section must be an object."))
            .ToList();
        ValidateRestoredLibraries(libraryRows, weirHome);

        await uow.ExecuteAsync("DELETE FROM libraries").ConfigureAwait(false);
        await uow.ExecuteAsync("DELETE FROM rule_sets").ConfigureAwait(false);
        var ruleColumns = await ColumnsAsync(uow, RuleSetsTable).ConfigureAwait(false);
        var libraryColumns = await ColumnsAsync(uow, LibrariesTable).ConfigureAwait(false);
        foreach (var row in Iterate(bundle.Get(RuleSetsTable) ?? new PyList()))
        {
            var data = row as PyDict ?? throw new PyTypeErrorException($"Each row in the backup's {RuleSetsTable} section must be an object.");
            await InsertAsync(uow, RuleSetsTable, ruleColumns, ToKwargs(ruleColumns, data)).ConfigureAwait(false);
        }

        foreach (var data in libraryRows)
        {
            if (restoredConnectionIds is not null)
            {
                ConfigurationBundleConnections.RemapLibraryConnection(data, restoredConnectionIds);
            }

            await InsertAsync(uow, LibrariesTable, libraryColumns, ToKwargs(libraryColumns, data)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The same validation <c>POST /processing/libraries</c> runs — folder overlap and the folder-safety
    /// rules, name uniqueness, and the closed enumerations — applied to every row before any of them is
    /// written. Throws <see cref="PyValueErrorException"/> naming the offending library and the problem.
    /// </summary>
    private static void ValidateRestoredLibraries(IReadOnlyList<PyDict> rows, string weirHome)
    {
        var inputs = rows.Select(RestoreLibraryInput).ToList();
        var allFolders = inputs
            .Select((input, index) => new OtherLibraryFolders(index, RestoreLibraryLabel(input, index), input.WatchedFolder, input.OutputFolder))
            .ToList();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < inputs.Count; index++)
        {
            var input = inputs[index];
            var label = RestoreLibraryLabel(input, index);
            try
            {
                LibraryRules.ValidateName(input.Name, seenNames);
                LibraryRules.ValidateScope(input.MediaType);
                var others = allFolders.Where(f => f.Id != index).ToList();
                LibraryRules.ValidateFolders(input.WatchedFolder, input.WorkFolder, input.OutputFolder, others, weirHome);
                LibraryRules.ValidateEnums(input);
            }
            catch (ProcessingLibraryException exception)
            {
                throw new PyValueErrorException($"{label}: {exception.Message}");
            }

            seenNames.Add(input.Name.Trim());
        }
    }

    /// <summary>The fields <see cref="LibraryRules"/> validates, read from a raw bundle row; everything else
    /// about the row (schedule, sorters, and so on) is restored as-is and needs no validation here.</summary>
    private static ProcessingLibraryInput RestoreLibraryInput(PyDict row) => new()
    {
        Name = RestoreLibraryString(row, "name") ?? string.Empty,
        MediaType = RestoreLibraryString(row, "media_type") ?? string.Empty,
        WatchedFolder = RestoreLibraryString(row, "watched_folder") ?? string.Empty,
        WorkFolder = RestoreLibraryString(row, "work_folder") ?? string.Empty,
        OutputFolder = RestoreLibraryString(row, "output_folder") ?? string.Empty,
        RejectedFileAction = RestoreLibraryString(row, "rejected_file_action") ?? RejectedFileActions.Leave,
        OutputCollisionPolicy = RestoreLibraryString(row, "output_collision_policy") ?? OutputCollisionPolicies.Replace,
        HardwareDecodeMode = RestoreLibraryString(row, "hardware_decode_mode") ?? HardwareDecodeModes.Off,
        FfmpegStrictness = RestoreLibraryString(row, "ffmpeg_strictness") ?? FfmpegStrictnessLevels.Normal,
        RemuxWriter = RestoreLibraryString(row, "remux_writer") ?? RemuxWriterChoice.Best,
        FailurePolicy = RestoreLibraryString(row, "failure_policy") ?? ProcessingFailurePolicies.PassThrough,
    };

    private static string? RestoreLibraryString(PyDict row, string key) => row.Get(key) is PyStr text ? text.Value : null;

    private static string RestoreLibraryLabel(ProcessingLibraryInput input, int index) =>
        string.IsNullOrWhiteSpace(input.Name) ? $"Library #{index + 1} in the backup" : $"Library '{input.Name.Trim()}'";
}
