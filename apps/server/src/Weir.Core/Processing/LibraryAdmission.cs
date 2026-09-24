using Weir.Core.Time;

namespace Weir.Core.Processing;

/// <summary>A library's admission rules read off its CSV/scalar columns.</summary>
public sealed record LibraryAdmissionRules(
    IReadOnlySet<string> MediaExtensions,
    IReadOnlySet<string> ExcludeMarkers,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns,
    long MinFileSizeMb,
    long MaxFileSizeMb,
    string RejectedFileAction,
    long MinFileAgeSeconds,
    DateTimeOffset? CreatedAfter,
    DateTimeOffset? CreatedBefore,
    DateTimeOffset? ModifiedAfter,
    DateTimeOffset? ModifiedBefore,
    bool ExcludeHidden,
    bool TopLevelOnly)
{
    private static IReadOnlyList<string> CsvValues(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : [.. csv.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0)];

    /// <summary>Read the rules off a library row, clamping sizes and ages to zero or more.</summary>
    public static LibraryAdmissionRules For(ProcessingLibraryRecord library)
    {
        ArgumentNullException.ThrowIfNull(library);
        return new LibraryAdmissionRules(
            MediaExtensions: CsvValues(library.MediaExtensionsCsv).Select(v => v.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal),
            ExcludeMarkers: CsvValues(library.ExcludeMarkersCsv).Select(v => v.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal),
            IncludePatterns: CsvValues(library.IncludePatternsCsv),
            ExcludePatterns: CsvValues(library.ExcludePatternsCsv),
            MinFileSizeMb: Math.Max(0, library.MinFileSizeMb),
            MaxFileSizeMb: Math.Max(0, library.MaxFileSizeMb),
            RejectedFileAction: string.Equals((library.RejectedFileAction ?? string.Empty).Trim(), "delete_file", StringComparison.OrdinalIgnoreCase) ? "delete_file" : "leave",
            MinFileAgeSeconds: Math.Max(0, library.MinFileAgeSeconds),
            CreatedAfter: library.CreatedAfter?.AsUtc,
            CreatedBefore: library.CreatedBefore?.AsUtc,
            ModifiedAfter: library.ModifiedAfter?.AsUtc,
            ModifiedBefore: library.ModifiedBefore?.AsUtc,
            ExcludeHidden: library.ExcludeHidden,
            TopLevelOnly: library.TopLevelOnly);
    }
}

/// <summary>A settled file the library rules refuse, and the counter it moves.</summary>
public sealed record LibraryAdmissionRejection(string Reason, string Counter);

/// <summary>Facts about one candidate file needed by the admission rejection check, independent of how
/// they were read from disk.</summary>
public sealed record CandidateFileFacts(long SizeBytes, DateTimeOffset? CreatedAt, DateTimeOffset? ModifiedAt);

public static class LibraryAdmission
{
    /// <summary>The plain-language reason and summary counter for a
    /// settled file the library's rules refuse, or <see langword="null"/> when it is admitted.</summary>
    public static LibraryAdmissionRejection? Rejection(
        string relativePath,
        string fileName,
        CandidateFileFacts facts,
        LibraryAdmissionRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var sizeMb = Math.Max(0, facts.SizeBytes) / (1024.0 * 1024.0);
        if (rules.MinFileSizeMb > 0 && facts.SizeBytes < rules.MinFileSizeMb * 1024 * 1024)
        {
            return new LibraryAdmissionRejection(
                $"Skipped because this file is {sizeMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB and the {rules.MinFileSizeMb} MB library minimum is not met.",
                "skipped_below_minimum_file_size");
        }

        if (rules.MaxFileSizeMb > 0 && facts.SizeBytes > rules.MaxFileSizeMb * 1024 * 1024)
        {
            return new LibraryAdmissionRejection(
                $"Skipped because this file is {sizeMb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} MB and exceeds the {rules.MaxFileSizeMb} MB library maximum.",
                "skipped_above_maximum_file_size");
        }

        if (facts.CreatedAt is { } createdAt)
        {
            if (rules.CreatedAfter is { } createdAfter && createdAt < createdAfter)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its filesystem creation time ({Timestamp.FromDateTimeOffset(createdAt).IsoFormat()}) is before this library's allowed window.",
                    "skipped_before_created_window");
            }

            if (rules.CreatedBefore is { } createdBefore && createdAt >= createdBefore)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its filesystem creation time ({Timestamp.FromDateTimeOffset(createdAt).IsoFormat()}) is after this library's allowed window.",
                    "skipped_after_created_window");
            }
        }

        if (facts.ModifiedAt is { } modifiedAt)
        {
            if (rules.ModifiedAfter is { } modifiedAfter && modifiedAt < modifiedAfter)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its last-modified time ({Timestamp.FromDateTimeOffset(modifiedAt).IsoFormat()}) is before this library's allowed window.",
                    "skipped_before_modified_window");
            }

            if (rules.ModifiedBefore is { } modifiedBefore && modifiedAt >= modifiedBefore)
            {
                return new LibraryAdmissionRejection(
                    $"Skipped because its last-modified time ({Timestamp.FromDateTimeOffset(modifiedAt).IsoFormat()}) is after this library's allowed window.",
                    "skipped_after_modified_window");
            }
        }

        var pathValues = new[] { relativePath.ToLowerInvariant(), fileName.ToLowerInvariant() };
        var includes = rules.IncludePatterns.Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
        if (includes.Count > 0 && !pathValues.Any(value => includes.Any(pattern => FnMatch(value, pattern))))
        {
            return new LibraryAdmissionRejection("Skipped because its path does not match this library's include patterns.", "skipped_by_include_pattern");
        }

        var excludes = rules.ExcludePatterns.Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
        if (pathValues.Any(value => excludes.Any(pattern => FnMatch(value, pattern))))
        {
            return new LibraryAdmissionRejection("Skipped because its path matches this library's exclude patterns.", "skipped_by_exclude_pattern");
        }

        return null;
    }

    /// <summary>Shell-style <c>*</c>/<c>?</c>/<c>[seq]</c> glob match, case-sensitive (callers lower-case both sides).</summary>
    internal static bool FnMatch(string value, string pattern) => Glob.IsMatch(value, pattern);
}
