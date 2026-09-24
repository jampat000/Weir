using Weir.Core.Rules;
using Weir.Core.Text;

namespace Weir.Core.LibraryMode;

/// <summary>How a scanned library file compares to its library's current rules (#505 point 2).</summary>
public enum LibraryFileClassification
{
    /// <summary>Already exactly what the rules would produce. Cleaning it would be a no-op.</summary>
    Matches,

    /// <summary>The rules would remove or reorder something. <see cref="LibraryFilePlanResult.Summary"/> says what.</summary>
    WouldChange,

    /// <summary>ffprobe could not read it, or no audio track would survive the plan. <see cref="LibraryFilePlanResult.Reason"/> says why.</summary>
    CannotProcess,
}

/// <summary>One file's classification, read-only (a scan never writes to the file it describes).</summary>
public sealed record LibraryFilePlanResult(
    LibraryFileClassification Classification,
    string? Summary,
    string? Reason,
    int RemovedAudioCount,
    int RemovedSubtitleCount,
    RemuxPlan? Plan,
    long EstimatedBytesSaved = 0,
    LibraryProblemKind? ProblemKind = null)
{
    public static LibraryFilePlanResult Matches() => new(LibraryFileClassification.Matches, null, null, 0, 0, null);

    /// <summary>
    /// <paramref name="problemKind"/> is what the #568 Problems view groups by; it is recorded alongside the
    /// sentence rather than parsed back out of it, so the wording stays free to change.
    /// </summary>
    public static LibraryFilePlanResult CannotProcess(string reason, LibraryProblemKind problemKind = LibraryProblemKind.Unreadable) =>
        new(LibraryFileClassification.CannotProcess, null, reason, 0, 0, null, 0, problemKind);

    /// <summary>
    /// <paramref name="estimatedBytesSaved"/> is a lower-bound estimate from removed tracks' own bit rate times the file's
    /// duration, read straight from ffprobe (never guessed from file size), so a track ffprobe gives no bit rate for is
    /// simply left out rather than approximated — an under-estimate is honest, an inflated one is not.
    /// </summary>
    public static LibraryFilePlanResult WouldChange(RemuxPlan plan, long estimatedBytesSaved = 0)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var pieces = new List<string>();
        if (plan.RemovedAudio.Count > 0)
        {
            pieces.Add($"remove {Plural.Of(plan.RemovedAudio.Count, "audio track")} ({string.Join(", ", plan.RemovedAudio)})");
        }

        if (plan.RemovedSubtitles.Count > 0)
        {
            pieces.Add($"remove {Plural.Of(plan.RemovedSubtitles.Count, "subtitle track")} ({string.Join(", ", plan.RemovedSubtitles)})");
        }

        if (plan.RemovedImages.Count > 0)
        {
            pieces.Add($"remove {Plural.Of(plan.RemovedImages.Count, "embedded image")}");
        }

        if (plan.RemovedAttachments.Count > 0)
        {
            pieces.Add($"remove {Plural.Of(plan.RemovedAttachments.Count, "attachment")}");
        }

        if (plan.MetadataNotes.Count > 0)
        {
            pieces.Add("strip metadata");
        }

        var summary = pieces.Count > 0 ? "Would " + string.Join("; ", pieces) + "." : "Would reorder or re-tag tracks without removing any.";
        return new LibraryFilePlanResult(LibraryFileClassification.WouldChange, summary, null, plan.RemovedAudio.Count, plan.RemovedSubtitles.Count, plan, estimatedBytesSaved);
    }
}

/// <summary>
/// Classifies one already-probed file against a library's rules, using exactly the same engine the download pipeline plans
/// with (<see cref="RemuxRules.PlanRemux"/>, <see cref="RemuxRules.IsRemuxRequired"/>) so "matches" and "would change" agree
/// with what the remux pass would actually do. Never writes anything: a scan only probes and plans.
/// </summary>
public static class LibraryFilePlanner
{
    public static LibraryFilePlanResult Classify(ProbeResult probe, ProcessingRulesConfig rules)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(rules);

        SplitProbeStreams split;
        IReadOnlyList<ProbeStreamInfo> attachments;
        try
        {
            split = RemuxRules.SplitStreams(probe);
            attachments = RemuxRules.AttachmentStreams(probe);
        }
        catch (RulesInputException exception)
        {
            return LibraryFilePlanResult.CannotProcess($"Weir could not read this file's tracks: {exception.Message}");
        }

        if (split.Video.Count == 0)
        {
            return LibraryFilePlanResult.CannotProcess("This file has no video track that Weir could find.", LibraryProblemKind.NoVideo);
        }

        RemuxPlan? plan;
        try
        {
            plan = RemuxRules.PlanRemux(split.Video, split.Audio, split.Subtitles, rules, attachments);
        }
        catch (RulesInputException exception)
        {
            return LibraryFilePlanResult.CannotProcess($"Weir could not plan this file against the library's rules: {exception.Message}");
        }

        if (plan is null)
        {
            return LibraryFilePlanResult.CannotProcess(
                "No audio track would remain after applying this library's rules, so Weir will not touch this file.",
                LibraryProblemKind.NoAudioLeft);
        }

        if (!RemuxRules.IsRemuxRequired(plan, split.Audio, split.Subtitles))
        {
            return LibraryFilePlanResult.Matches();
        }

        return LibraryFilePlanResult.WouldChange(plan, EstimateSavings(probe, split, plan));
    }

    /// <summary>
    /// What this plan would reclaim from this file. Public because a plan a person chose themselves is measured the
    /// same way as one the rules chose: the estimate describes the file and the plan, not who decided.
    /// </summary>
    public static long EstimateSavings(ProbeResult probe, SplitProbeStreams split, RemuxPlan plan)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(plan);
        var keptAudioIndices = plan.Audio.Select(t => t.InputIndex).ToHashSet();
        var keptSubtitleIndices = plan.Subtitles.Select(t => t.InputIndex).ToHashSet();
        return EstimateRemovedBytes(probe, split.Audio, keptAudioIndices) +
            EstimateRemovedBytes(probe, split.Subtitles, keptSubtitleIndices);
    }

    /// <summary>
    /// What the tracks a plan drops are taking up, added together (#648). Each track is measured by the best evidence the
    /// file offers, in this order: the exact byte count Matroska records per track, the per-track bit rate it records,
    /// then ffprobe's own <c>bit_rate</c>, which Matroska rarely reports. A track that offers none of those adds nothing,
    /// so this stays an honest under-estimate rather than a guess.
    /// </summary>
    private static long EstimateRemovedBytes(ProbeResult probe, IReadOnlyList<ProbeStreamInfo> streams, HashSet<int> keptInputIndices)
    {
        var durationSeconds = 0.0;
        if (probe.Json.ValueKind == System.Text.Json.JsonValueKind.Object &&
            probe.Json.TryGetProperty("format", out var format) && format.ValueKind == System.Text.Json.JsonValueKind.Object &&
            format.TryGetProperty("duration", out var durationValue) && durationValue.ValueKind == System.Text.Json.JsonValueKind.String &&
            double.TryParse(durationValue.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsedDuration))
        {
            durationSeconds = parsedDuration;
        }

        long total = 0;
        foreach (var stream in streams)
        {
            if (stream.Index is { } index && keptInputIndices.Contains((int)index))
            {
                continue;
            }

            total += RemovedStreamBytes(stream, durationSeconds);
        }

        return total;
    }

    /// <summary>One dropped track's size, or 0 when the file does not say.</summary>
    private static long RemovedStreamBytes(ProbeStreamInfo stream, double durationSeconds)
    {
        // mkvmerge writes the exact byte count of each track; nothing beats being told.
        if (StatisticsTag(stream, "NUMBER_OF_BYTES") is { } bytes)
        {
            return bytes;
        }

        if (durationSeconds <= 0)
        {
            return 0;
        }

        if (StatisticsTag(stream, "BPS") is { } bitsPerSecondTag)
        {
            return (long)(bitsPerSecondTag * durationSeconds / 8.0);
        }

        return stream.Get("bit_rate") is { } bitRateValue && RulesJson.TryInt(bitRateValue, out var bitsPerSecond) && bitsPerSecond > 0
            ? (long)(bitsPerSecond * durationSeconds / 8.0)
            : 0;
    }

    /// <summary>
    /// A Matroska statistics tag, with or without the language suffix mkvmerge adds (<c>BPS</c>, <c>BPS-eng</c>). Tag
    /// names are matched without regard to case, as ffprobe has reported them both ways.
    /// </summary>
    private static long? StatisticsTag(ProbeStreamInfo stream, string name)
    {
        foreach (var (key, value) in stream.Tags)
        {
            if (!key.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
                (key.Length != name.Length && key[name.Length] != '-'))
            {
                continue;
            }

            if (long.TryParse(
                    value.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed) &&
                parsed > 0)
            {
                return parsed;
            }
        }

        return null;
    }
}
