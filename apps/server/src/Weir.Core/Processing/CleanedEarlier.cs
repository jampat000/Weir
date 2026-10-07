using System.Globalization;
using Weir.Core.Json;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Core.Processing;

/// <summary>
/// A source file Weir has already cleaned, and what became of the copy it wrote. A repeat of the same source (same
/// path, size and modification time) is never processed again: it settles as a skip that says this.
/// </summary>
/// <param name="OutputPath">The cleaned copy Weir wrote, as Weir sees it.</param>
/// <param name="CleanedAt">When that copy was written.</param>
/// <param name="Collected">The copy is gone because a media manager imported it.</param>
/// <param name="Manager">Which manager collected it, when it said.</param>
/// <param name="CollectedAt">When the manager said it collected the copy.</param>
public sealed record CleanedEarlier(string OutputPath, DateTimeOffset CleanedAt, bool Collected, string? Manager, DateTimeOffset? CollectedAt)
{
    /// <summary>The result key that marks a pass settled as a repeat, so the report to the manager and Activity can say so.</summary>
    public const string ResultKey = "already_cleaned";

    /// <summary>What Weir tells a media manager about a repeat: the cleaned copy is the one it was already told about.</summary>
    public const string ReportMessage = "Weir had already cleaned this exact file, so it did not process it again; the cleaned copy is the one reported before.";

    /// <summary>"Already done: cleaned on 2026-10-07 into D:\Clean\Film.mkv", or the same for a copy a manager has collected.</summary>
    public string Reason => Collected
        ? $"Already imported: {Manager ?? "your media manager"} collected the cleaned copy on {Day(CollectedAt ?? CleanedAt)}"
        : $"Already done: cleaned on {Day(CleanedAt)} into {OutputPath}";

    /// <summary>The Activity line: "Skipped: already done (Film.mkv)".</summary>
    public string Title(string fileName) => $"Skipped: {(Collected ? "already imported" : "already done")} ({fileName})";

    /// <summary>The result a finished pass would report, for the repeat that was not run: a completion naming the earlier copy.</summary>
    public WireObject ToResult(string relativePath, long? libraryId, string? outputFolder)
    {
        var result = new WireObject()
            .Set("ok", true)
            .Set("outcome", RemuxPassOutcomes.LiveOutputWritten)
            .Set("relative_media_path", relativePath)
            .Set("library_id", libraryId)
            .Set("output_file", OutputPath)
            .Set("reason", Reason)
            .Set(ResultKey, true);
        if (!string.IsNullOrWhiteSpace(outputFolder))
        {
            result.Set("processing_output_folder_resolved", outputFolder);
        }

        return result;
    }

    private static string Day(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
