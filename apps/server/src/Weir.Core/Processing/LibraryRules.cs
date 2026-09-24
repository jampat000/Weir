using Weir.Core.Jobs;
using Weir.Core.Media;
using Weir.Core.Rules;

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
public static class LibraryRules
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

    /// <summary>A library's folder path, normalized for the overlap check.</summary>
    public static string? NormalizeFolder(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var slashed = text.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        return slashed.Length == 0 ? "/" : slashed;
    }

    /// <summary>Equal, or one a path-segment ancestor of the other.</summary>
    public static bool FoldersOverlap(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        return IsAncestor(a, b) || IsAncestor(b, a);
    }

    private static bool IsAncestor(string ancestor, string descendant) =>
        descendant.Length > ancestor.Length &&
        descendant.StartsWith(ancestor, StringComparison.Ordinal) &&
        (ancestor == "/" || descendant[ancestor.Length] == '/');

    /// <summary>The Linux directories a watched, work, output or library folder may never be, or be inside.</summary>
    private static readonly string[] LinuxReservedRoots =
        ["/etc", "/usr", "/bin", "/sbin", "/boot", "/proc", "/sys", "/dev", "/var/lib"];

    /// <summary>
    /// A folder a library create/update/restore is about to save: must be absolute, may not contain a
    /// <c>..</c> segment, and may not be a drive or filesystem root, Weir's own data folder, a Windows
    /// system folder, or one of a fixed list of Linux system folders. Applied only to the folder being
    /// saved right now, so a library already pointing at such a folder from before this rule existed
    /// keeps working until it is next saved.
    /// </summary>
    /// <param name="label">What the folder is for, used in the exception message ("watched", "output", ...).</param>
    /// <param name="raw">The folder path as given; empty or whitespace is a no-op.</param>
    /// <param name="weirHome">Weir's own data folder; null skips that one check.</param>
    public static void ValidateFolderPath(string label, string? raw, string? weirHome = null)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (HasParentSegment(text))
        {
            throw new ProcessingLibraryException($"The {label} folder can't contain a '..' segment. Enter the folder's full path instead.");
        }

        if (!LooksAbsolute(text))
        {
            throw new ProcessingLibraryException($"The {label} folder must be an absolute path.");
        }

        var windows = LooksLikeWindowsPath(text);
        var full = MediaToolLocations.Normalize(text, windows);
        RejectReservedFolder(label, full, windows, weirHome);
    }

    /// <summary>A path segment equal to <c>..</c>, under either separator style.</summary>
    private static bool HasParentSegment(string text) =>
        text.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..");

    /// <summary>A Unix root, a Windows drive-letter path, or a rooted/UNC path starting with a backslash.</summary>
    private static bool LooksAbsolute(string text) =>
        text.StartsWith('/') || text.StartsWith('\\') ||
        (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/'));

    /// <summary>A drive-letter or backslash-rooted path is Windows-form; everything else is Unix-form.</summary>
    private static bool LooksLikeWindowsPath(string text) =>
        text.StartsWith('\\') || (text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':');

    private static void RejectReservedFolder(string label, string fullPath, bool windows, string? weirHome)
    {
        if (IsDriveOrFilesystemRoot(fullPath, windows))
        {
            throw new ProcessingLibraryException($"Weir can't use the root of a drive as a {label} folder. Choose a folder inside it.");
        }

        if (!string.IsNullOrWhiteSpace(weirHome) && IsSameOrInside(fullPath, MediaToolLocations.Normalize(weirHome.Trim(), windows), windows))
        {
            throw new ProcessingLibraryException($"Weir can't use its own data folder as a {label} folder. Choose a different folder.");
        }

        if (windows)
        {
            foreach (var special in WindowsReservedRoots())
            {
                if (IsSameOrInside(fullPath, special, windows))
                {
                    throw new ProcessingLibraryException($"Weir can't use a Windows system folder as a {label} folder. Choose a folder outside it.");
                }
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile) && PathsEqual(fullPath, MediaToolLocations.Normalize(userProfile, windows), windows))
            {
                throw new ProcessingLibraryException($"Weir can't use your whole user profile folder as a {label} folder. Choose a folder inside it.");
            }
        }
        else
        {
            foreach (var reserved in LinuxReservedRoots)
            {
                if (IsSameOrInside(fullPath, reserved, windows))
                {
                    throw new ProcessingLibraryException($"Weir can't use a system folder as a {label} folder. Choose a folder outside it.");
                }
            }
        }
    }

    private static bool IsDriveOrFilesystemRoot(string fullPath, bool windows)
    {
        if (!windows)
        {
            return fullPath == "/";
        }

        var trimmed = fullPath.TrimEnd('\\');
        return trimmed.Length == 2 && char.IsAsciiLetter(trimmed[0]) && trimmed[1] == ':';
    }

    private static bool PathsEqual(string a, string b, bool windows) =>
        string.Equals(a, b, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>Whether <paramref name="fullPath"/> is <paramref name="reserved"/> itself or a folder inside it.</summary>
    private static bool IsSameOrInside(string fullPath, string reserved, bool windows)
    {
        var trimmedReserved = reserved.TrimEnd('\\', '/');
        if (PathsEqual(fullPath, trimmedReserved, windows))
        {
            return true;
        }

        var withSeparator = trimmedReserved + (windows ? '\\' : '/');
        return fullPath.StartsWith(withSeparator, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>The Windows directory and both Program Files folders, normalized; empty (non-Windows host) entries are skipped.</summary>
    private static IEnumerable<string> WindowsReservedRoots()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(path))
            {
                yield return MediaToolLocations.Normalize(path, windows: true);
            }
        }
    }

    /// <summary>
    /// A library's own three folders must be distinct, and its watched/output
    /// folders must not overlap any other library's watched/output folders.
    /// </summary>
    /// <param name="watchedFolder">The library's watched folder, or empty/null when unset.</param>
    /// <param name="workFolder">The library's work folder, or empty/null when unset.</param>
    /// <param name="outputFolder">The library's output folder, or empty/null when unset.</param>
    /// <param name="others">Every other library's watched/output folders, for the overlap check.</param>
    /// <param name="weirHome">Weir's own data folder, refused as a library folder (see
    /// <see cref="ValidateFolderPath"/>); null skips that one check, for callers that don't have it.</param>
    public static void ValidateFolders(
        string? watchedFolder,
        string? workFolder,
        string? outputFolder,
        IReadOnlyList<OtherLibraryFolders> others,
        string? weirHome = null)
    {
        ValidateFolderPath("watched", watchedFolder, weirHome);
        ValidateFolderPath("work", workFolder, weirHome);
        ValidateFolderPath("output", outputFolder, weirHome);

        var watched = NormalizeFolder(watchedFolder);
        var work = NormalizeFolder(workFolder);
        var output = NormalizeFolder(outputFolder);

        if (watched is not null && output is null)
        {
            throw new ProcessingLibraryException("Set an output folder as well as a watched folder, so processed files have somewhere to go.");
        }

        foreach (var (labelA, a, labelB, b) in new[]
                 {
                     ("watched", watched, "output", output),
                     ("watched", watched, "work", work),
                     ("work", work, "output", output),
                 })
        {
            if (a is not null && b is not null && FoldersOverlap(a, b))
            {
                throw new ProcessingLibraryException(
                    $"This library's {labelA} folder and {labelB} folder overlap. Use separate folders, neither inside the other.");
            }
        }

        foreach (var other in others)
        {
            foreach (var (labelA, a) in new[] { ("watched", watched), ("output", output) })
            {
                foreach (var (labelB, raw) in new[] { ("watched", other.WatchedFolder), ("output", other.OutputFolder) })
                {
                    var b = NormalizeFolder(raw);
                    if (a is not null && b is not null && FoldersOverlap(a, b))
                    {
                        throw new ProcessingLibraryException(
                            $"This library's {labelA} folder overlaps the {labelB} folder of '{other.Name}'. " +
                            "Each library needs its own folders, neither inside another's.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The closed enumerations a create/update request already validates at the HTTP boundary
    /// (<c>BodyModel.Literal</c>, in <c>ProcessingLibraryEndpoints.ReadLibraryBody</c>). Restoring a
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

    /// <summary>The fields a rule-set create/update request carries.</summary>
    public sealed record RuleSetInput
    {
        public required string Name { get; init; }
        public string PrimaryAudioLang { get; init; } = string.Empty;
        public string SecondaryAudioLang { get; init; } = string.Empty;
        public string TertiaryAudioLang { get; init; } = string.Empty;
        public string DefaultAudioSlot { get; init; } = "primary";
        public bool RemoveCommentary { get; init; }
        public string SubtitleMode { get; init; } = "keep_all";
        public string SubtitleLangsCsv { get; init; } = string.Empty;
        public bool PreserveForcedSubs { get; init; } = true;
        public bool PreserveDefaultSubs { get; init; } = true;
        public string AudioSortersJson { get; init; } = string.Empty;
        public string SubtitleSortersJson { get; init; } = string.Empty;
        public bool KeepOriginalLanguage { get; init; }
        public string OriginalLanguageAdditionalCsv { get; init; } = string.Empty;
        public bool OriginalLanguageKeepOnlyFirst { get; init; } = true;
        public bool OriginalLanguageFirstIfNone { get; init; } = true;
        public bool OriginalLanguageTreatEmptyAsOriginal { get; init; }
        public bool RemoveImages { get; init; }
        public bool RemoveAttachments { get; init; }
        public bool RemoveTitle { get; init; }
        public bool RemoveLanguageTags { get; init; }
        public bool RemoveOtherMetadata { get; init; }
        public string AudioPreferenceMode { get; init; } = "preferred_langs_quality";

        /// <summary>Issue #495.</summary>
        public bool RemoveHearingImpairedSubs { get; init; }

        /// <summary>Issue #497.</summary>
        public string AudioKeepMode { get; init; } = RemuxRuleValues.AudioKeepModeSingle;

        /// <summary>Issue #497.</summary>
        public int SubtitleMaxPerLanguage { get; init; }

        /// <summary>Issue #497.</summary>
        public string SubtitleQualityStrategy { get; init; } = RemuxRuleValues.SubtitleStrategyTextFirst;

        /// <summary>Issue #498.</summary>
        public bool StandardizeTrackNames { get; init; }

        /// <summary>Issue #498.</summary>
        public string TrackNameTemplate { get; init; } = TrackNaming.DefaultTemplate;

        /// <summary>Issue #498.</summary>
        public TrackNameOverrides TrackNameOverrides { get; init; } = new();

        /// <summary>Issue #498.</summary>
        public bool ClearVideoTrackNames { get; init; }

        /// <summary>Issue #498.</summary>
        public bool RemoveChapters { get; init; }
    }

    /// <summary>Applies validated rule-set fields, including the audio and subtitle sorters, onto a rule-set record.</summary>
    public static ProcessingRuleSetRecord ApplyRuleSetFields(ProcessingRuleSetRecord row, RuleSetInput body)
    {
        string audioSorters;
        try
        {
            audioSorters = TrackSorters.Validate(body.AudioSortersJson);
        }
        catch (TrackSorterException exception)
        {
            throw new ProcessingLibraryException(exception.Message);
        }

        string subtitleSorters;
        try
        {
            subtitleSorters = TrackSorters.Validate(body.SubtitleSortersJson);
        }
        catch (TrackSorterException exception)
        {
            throw new ProcessingLibraryException(exception.Message);
        }

        // AudioKeepMode and SubtitleQualityStrategy are closed enumerations validated at the HTTP boundary
        // (BodyModel.Literal, like DefaultAudioSlot/SubtitleMode/AudioPreferenceMode); SubtitleMaxPerLanguage's
        // "no negative" rule is validated the same way (BodyModel.Number's ge: 0). Nothing further to check here.
        var metadataForValidation = new MetadataRules
        {
            TrackNameTemplate = body.TrackNameTemplate,
            TrackNameOverrides = body.TrackNameOverrides,
        };
        try
        {
            TrackNaming.ValidateAll(metadataForValidation);
        }
        catch (TrackNameTemplateException exception)
        {
            throw new ProcessingLibraryException(exception.Message);
        }

        // No preset fallback for an empty audio sorter list is needed: TrackSorters.Validate never
        // returns an empty string, because empty input already becomes the default sorter list.
        return row with
        {
            PrimaryAudioLang = body.PrimaryAudioLang,
            SecondaryAudioLang = body.SecondaryAudioLang,
            TertiaryAudioLang = body.TertiaryAudioLang,
            DefaultAudioSlot = body.DefaultAudioSlot,
            RemoveCommentary = body.RemoveCommentary,
            SubtitleMode = body.SubtitleMode,
            SubtitleLangsCsv = body.SubtitleLangsCsv,
            PreserveForcedSubs = body.PreserveForcedSubs,
            PreserveDefaultSubs = body.PreserveDefaultSubs,
            AudioPreferenceMode = body.AudioPreferenceMode,
            KeepOriginalLanguage = body.KeepOriginalLanguage,
            OriginalLanguageAdditionalCsv = body.OriginalLanguageAdditionalCsv,
            OriginalLanguageKeepOnlyFirst = body.OriginalLanguageKeepOnlyFirst,
            OriginalLanguageFirstIfNone = body.OriginalLanguageFirstIfNone,
            OriginalLanguageTreatEmptyAsOriginal = body.OriginalLanguageTreatEmptyAsOriginal,
            RemoveImages = body.RemoveImages,
            RemoveAttachments = body.RemoveAttachments,
            RemoveTitle = body.RemoveTitle,
            RemoveLanguageTags = body.RemoveLanguageTags,
            RemoveOtherMetadata = body.RemoveOtherMetadata,
            AudioSortersJson = audioSorters,
            SubtitleSortersJson = subtitleSorters,
            RemoveHearingImpairedSubs = body.RemoveHearingImpairedSubs,
            AudioKeepMode = body.AudioKeepMode,
            SubtitleMaxPerLanguage = body.SubtitleMaxPerLanguage,
            SubtitleQualityStrategy = body.SubtitleQualityStrategy,
            StandardizeTrackNames = body.StandardizeTrackNames,
            TrackNameTemplate = body.TrackNameTemplate,
            TrackNameOverrides = body.TrackNameOverrides,
            ClearVideoTrackNames = body.ClearVideoTrackNames,
            RemoveChapters = body.RemoveChapters,
        };
    }
}
