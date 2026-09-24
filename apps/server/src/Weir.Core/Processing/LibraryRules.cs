using Weir.Core.Jobs;
using Weir.Core.Media;

namespace Weir.Core.Processing;

/// <summary>A library or rule set could not be saved or removed.</summary>
public sealed class ProcessingLibraryException : Exception
{
    public ProcessingLibraryException(string message)
        : base(message)
    {
    }
}

/// <summary>The fields a library create/update request carries.</summary>
public sealed record ProcessingLibraryInput
{
    public required string Name { get; init; }
    public required string MediaType { get; init; }
    public bool Enabled { get; init; } = true;
    public string WatchedFolder { get; init; } = string.Empty;
    public string WorkFolder { get; init; } = string.Empty;
    public string OutputFolder { get; init; } = string.Empty;
    public string MediaExtensionsCsv { get; init; } = string.Empty;
    public string ExcludeMarkersCsv { get; init; } = string.Empty;
    public string IncludePatternsCsv { get; init; } = string.Empty;
    public string ExcludePatternsCsv { get; init; } = string.Empty;
    public long MinFileSizeMb { get; init; }
    public long MaxFileSizeMb { get; init; }
    public string RejectedFileAction { get; init; } = "leave";
    public long MinFileAgeSeconds { get; init; } = 60;
    public Time.PyDateTime? CreatedAfter { get; init; }
    public Time.PyDateTime? CreatedBefore { get; init; }
    public Time.PyDateTime? ModifiedAfter { get; init; }
    public Time.PyDateTime? ModifiedBefore { get; init; }
    public bool ExcludeHidden { get; init; } = true;
    public bool TopLevelOnly { get; init; }
    public string SidecarPatternsCsv { get; init; } = ".srt,.ass,.ssa,.sub,.idx,.vtt,.nfo,.jpg,.png";
    public bool PreserveOriginalTimestamps { get; init; }
    public string OutputCollisionPolicy { get; init; } = "replace";
    public string HardwareDecodeMode { get; init; } = "off";
    public string HardwareDevice { get; init; } = string.Empty;
    public string HardwareDisabledVendorsCsv { get; init; } = string.Empty;
    public string FfmpegStrictness { get; init; } = "normal";
    public string RemuxWriter { get; init; } = RemuxWriterChoice.Best;
    public bool RewriteWithFfmpeg { get; init; } = true;
    public long ScanIntervalSeconds { get; init; } = 300;
    public long HoldMinutes { get; init; }
    public long FileDetectionIntervalSeconds { get; init; } = 30;
    public bool IgnoreSizeChanges { get; init; }
    public bool SkipAccessTests { get; init; }
    public long MaxAttempts { get; init; } = 3;
    public long RetryBackoffSeconds { get; init; } = 300;
    public bool RetryExecutionFailures { get; init; } = true;
    public string FailurePolicy { get; init; } = ProcessingFailurePolicies.PassThrough;
    public string ScheduleGrid { get; init; } = string.Empty;
    public bool RetryPreflightFailures { get; init; }
    public bool FileSystemEventsEnabled { get; init; } = true;
    public bool ScheduleEnabled { get; init; } = true;
    public bool ScheduleHoursLimited { get; init; }
    public string ScheduleDays { get; init; } = string.Empty;
    public string ScheduleStart { get; init; } = "00:00";
    public string ScheduleEnd { get; init; } = "23:59";
    public long MaxConcurrentFiles { get; init; } = OperatorSettingsRules.LibraryFollowsFilesAtOnce;
    public long Priority { get; init; }
    public long? RuleSetId { get; init; }
    public IReadOnlyList<long> ManagerConnectionIds { get; init; } = [];

    /// <summary><c>remove_original_after_success</c>; see <see cref="ProcessingLibraryRecord.RemoveOriginalAfterSuccess"/>.</summary>
    public bool RemoveOriginalAfterSuccess { get; init; } = true;
}

/// <summary>One other library's folders, for the overlap check.</summary>
public sealed record OtherLibraryFolders(long Id, string Name, string WatchedFolder, string OutputFolder);

/// <summary>
/// Pure Processing library/rule-set validation.
/// The store applies these to rows and does the actual reads/writes.
/// </summary>
public static partial class LibraryRules
{
    /// <summary>A known media scope, trimmed and lower-cased.</summary>
    public static string ValidateScope(string? mediaType)
    {
        var scope = (mediaType ?? string.Empty).Trim().ToLowerInvariant();
        if (!ProcessingMediaScopes.All.Contains(scope, StringComparer.Ordinal))
        {
            throw new ProcessingLibraryException($"Unknown media type '{mediaType}'. Use one of: {string.Join(", ", ProcessingMediaScopes.All)}.");
        }

        return scope;
    }

    /// <summary>A non-empty, unique library name. The caller has already excluded the row being saved from <paramref name="existingNames"/>.</summary>
    public static string ValidateName(string? name, IReadOnlyCollection<string> existingNames)
    {
        var label = (name ?? string.Empty).Trim();
        if (label.Length == 0)
        {
            throw new ProcessingLibraryException("Give the library a name so you can tell it apart later.");
        }

        if (existingNames.Contains(label, StringComparer.Ordinal))
        {
            throw new ProcessingLibraryException($"A library named '{label}' already exists.");
        }

        return label;
    }

    /// <summary>
    /// The closed enumerations a create/update request already validates at the HTTP boundary
    /// (<c>BodyModel.Literal</c>, in <c>ProcessingLibraryMapping.ReadLibraryBody</c>). Restoring a
    /// configuration bundle writes rows directly rather than going through that endpoint, so it calls
    /// this instead, against the same allowed-value lists, to get the same guarantee.
    /// </summary>
    public static void ValidateEnums(ProcessingLibraryInput body)
    {
        RequireOneOf("rejected file action", body.RejectedFileAction, RejectedFileActions.All);
        RequireOneOf("output collision policy", body.OutputCollisionPolicy, OutputCollisionPolicies.All);
        RequireOneOf("hardware decode mode", body.HardwareDecodeMode, HardwareDecodeModes.All);
        RequireOneOf("ffmpeg strictness level", body.FfmpegStrictness, FfmpegStrictnessLevels.All);
        RequireOneOf("remux writer", body.RemuxWriter, RemuxWriterChoice.All);
        RequireOneOf("failure policy", body.FailurePolicy, ProcessingFailurePolicies.All);
    }

    private static void RequireOneOf(string label, string value, IReadOnlyList<string> allowed)
    {
        if (!allowed.Contains(value, StringComparer.Ordinal))
        {
            throw new ProcessingLibraryException($"'{value}' is not a valid {label}. Use one of: {string.Join(", ", allowed)}.");
        }
    }

    /// <summary>Linked manager connections as unique, ascending ids; all must exist.</summary>
    public static IReadOnlyList<long> ValidateManagerConnections(IReadOnlyList<long> connectionIds, IReadOnlySet<long> knownConnectionIds)
    {
        var unique = connectionIds.Distinct().Order().ToList();
        if (unique.Count == 0)
        {
            return [];
        }

        var missing = unique.Where(id => !knownConnectionIds.Contains(id)).ToList();
        if (missing.Count > 0)
        {
            throw new ProcessingLibraryException($"No media manager connection with id {missing[0]}. Add the connection first, then link it.");
        }

        return unique;
    }

    /// <summary>The rule set id, if any, provided it exists.</summary>
    public static long? ValidateRuleSet(long? ruleSetId, bool exists)
    {
        if (ruleSetId is null)
        {
            return null;
        }

        if (!exists)
        {
            throw new ProcessingLibraryException($"No rule set with id {ruleSetId}.");
        }

        return ruleSetId;
    }

    /// <summary>Applies validated fields onto a library record, without persisting it.</summary>
    public static ProcessingLibraryRecord ApplyFields(ProcessingLibraryRecord row, ProcessingLibraryInput body, bool ruleSetExists)
    {
        string grid;
        try
        {
            grid = ScheduleGrid.Normalize(body.ScheduleGrid);
        }
        catch (ScheduleGridException exception)
        {
            throw new ProcessingLibraryException(exception.Message);
        }

        return row with
        {
            Enabled = body.Enabled,
            WatchedFolder = body.WatchedFolder,
            WorkFolder = body.WorkFolder,
            OutputFolder = body.OutputFolder,
            MediaExtensionsCsv = body.MediaExtensionsCsv,
            ExcludeMarkersCsv = body.ExcludeMarkersCsv,
            IncludePatternsCsv = body.IncludePatternsCsv,
            ExcludePatternsCsv = body.ExcludePatternsCsv,
            MinFileSizeMb = body.MinFileSizeMb,
            MaxFileSizeMb = body.MaxFileSizeMb,
            RejectedFileAction = body.RejectedFileAction,
            MinFileAgeSeconds = body.MinFileAgeSeconds,
            CreatedAfter = body.CreatedAfter,
            CreatedBefore = body.CreatedBefore,
            ModifiedAfter = body.ModifiedAfter,
            ModifiedBefore = body.ModifiedBefore,
            ExcludeHidden = body.ExcludeHidden,
            TopLevelOnly = body.TopLevelOnly,
            ScanIntervalSeconds = body.ScanIntervalSeconds,
            HoldMinutes = body.HoldMinutes,
            SidecarPatternsCsv = body.SidecarPatternsCsv,
            PreserveOriginalTimestamps = body.PreserveOriginalTimestamps,
            OutputCollisionPolicy = body.OutputCollisionPolicy,
            HardwareDecodeMode = body.HardwareDecodeMode,
            HardwareDevice = body.HardwareDevice,
            HardwareDisabledVendorsCsv = body.HardwareDisabledVendorsCsv,
            FfmpegStrictness = body.FfmpegStrictness,
            FileDetectionIntervalSeconds = body.FileDetectionIntervalSeconds,
            IgnoreSizeChanges = body.IgnoreSizeChanges,
            SkipAccessTests = body.SkipAccessTests,
            FileSystemEventsEnabled = body.FileSystemEventsEnabled,
            MaxAttempts = body.MaxAttempts,
            RetryBackoffSeconds = body.RetryBackoffSeconds,
            RetryExecutionFailures = body.RetryExecutionFailures,
            RetryPreflightFailures = body.RetryPreflightFailures,
            FailurePolicy = body.FailurePolicy,
            ScheduleEnabled = body.ScheduleEnabled,
            ScheduleHoursLimited = body.ScheduleHoursLimited,
            ScheduleDays = body.ScheduleDays,
            ScheduleStart = body.ScheduleStart,
            ScheduleEnd = body.ScheduleEnd,
            MaxConcurrentFiles = body.MaxConcurrentFiles,
            Priority = body.Priority,
            ScheduleGrid = grid,
            RuleSetId = ValidateRuleSet(body.RuleSetId, ruleSetExists),
            RemoveOriginalAfterSuccess = body.RemoveOriginalAfterSuccess,
        };
    }

}
