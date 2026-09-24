using System.Globalization;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Observability;
using Weir.Core.Rules;

namespace Weir.Core.Processing.RemuxPass;

/// <summary>The job kind and outcomes of one per-file pass.</summary>
public static class RemuxPassOutcomes
{
    /// <summary>Durable job kind for one per-file remux pass.</summary>
    public const string JobKind = "processing.file.remux_pass.v1";

    public const string LiveOutputWritten = "live_output_written";
    public const string LiveSkippedNotRequired = "live_skipped_not_required";
    public const string SkippedGuardrail = "skipped_guardrail";
    public const string SourceNotReady = "source_not_ready";
    public const string FailedBeforeExecution = "failed_before_execution";
    public const string FailedDuringExecution = "failed_during_execution";
}

/// <summary>Operator-facing strings for a pass.</summary>
public static class RemuxPassVisibility
{
    private const int MaxArgvForActivity = 64;

    /// <summary>A compact plan summary, not a full ffmpeg command.</summary>
    public static string SummarizeRemuxPlan(RemuxPlan plan, int maxLength = 600)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var audio = plan.Audio.Count == 0 ? "none" : string.Join(", ", plan.Audio.Select(Track));
        var subtitles = plan.Subtitles.Count == 0 ? "none" : string.Join(", ", plan.Subtitles.Select(Track));
        var removedAudio = string.Join("; ", plan.RemovedAudio.Take(6));
        if (plan.RemovedAudio.Count > 6)
        {
            removedAudio += $" (+{(plan.RemovedAudio.Count - 6).ToString(CultureInfo.InvariantCulture)} more)";
        }

        var removedSubtitles = string.Join("; ", plan.RemovedSubtitles.Take(6));
        if (plan.RemovedSubtitles.Count > 6)
        {
            removedSubtitles += $" (+{(plan.RemovedSubtitles.Count - 6).ToString(CultureInfo.InvariantCulture)} more)";
        }

        var parts = new List<string>
        {
            $"video copy indices: {IntListRepr(plan.VideoIndices)}",
            $"audio out: {audio}",
            $"subtitles out: {subtitles}",
        };
        if (removedAudio.Length > 0)
        {
            parts.Add($"removed audio: {removedAudio}");
        }

        if (removedSubtitles.Length > 0)
        {
            parts.Add($"removed subs: {removedSubtitles}");
        }

        var summary = string.Join(" | ", parts);
        return WireStrings.Length(summary) > maxLength ? WireStrings.Slice(summary, maxLength - 12) + "…(truncated)" : summary;

        static string Track(PlannedTrack track) =>
            $"#{track.InputIndex.ToString(CultureInfo.InvariantCulture)} {(track.LangLabel.Length > 0 ? track.LangLabel : "und")}";
    }

    /// <summary>A list of ints written as <c>[0, 2]</c>, the form stored plan summaries already use.</summary>
    public static string IntListRepr(IEnumerable<int> values) =>
        "[" + string.Join(", ", values.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";

    /// <summary>One plain line, with the file name when there is one.</summary>
    public static string ActivityTitle(WireObject payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var name = payload.Get("relative_media_path") is WireString rel && WireStrings.Strip(rel.Value).Length > 0
            ? MediaPathNames.Name(rel.Value, OperatingSystem.IsWindows())
            : "unknown file";
        var outcome = payload.Get("outcome") is WireString text ? text.Value : null;
        if (payload.Get("pass_through_unchanged") is WireBool { Value: true } && outcome == RemuxPassOutcomes.LiveSkippedNotRequired)
        {
            return $"{name} was passed through unchanged";
        }

        return outcome switch
        {
            RemuxPassOutcomes.LiveOutputWritten => $"{name} was processed successfully",
            RemuxPassOutcomes.LiveSkippedNotRequired => $"No changes needed for {name}",
            RemuxPassOutcomes.SkippedGuardrail => $"Skipped {name}",
            RemuxPassOutcomes.SourceNotReady => $"Waiting for {name}",
            RemuxPassOutcomes.FailedDuringExecution => $"{name} could not be processed",
            _ when outcome == RemuxPassOutcomes.FailedBeforeExecution || payload.Get("ok") is WireBool { Value: false } => $"{name} could not be checked",
            _ => "File processing finished",
        };
    }

    /// <summary>
    /// The diagnostic envelope, and the ffmpeg argv bounded so Activity JSON
    /// stays under typical row limits.
    /// </summary>
    public static WireObject ClipForActivity(WireObject payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var output = payload.Copy();
        var outcome = output.Get("outcome") is { IsTruthy: true } value ? WireConvert.Str(value) : string.Empty;
        string result;
        if (outcome is RemuxPassOutcomes.LiveOutputWritten or RemuxPassOutcomes.LiveSkippedNotRequired)
        {
            result = "success";
        }
        else if (outcome is RemuxPassOutcomes.SkippedGuardrail or RemuxPassOutcomes.SourceNotReady)
        {
            result = "skipped";
        }
        else
        {
            result = output.Get("ok") is WireBool { Value: false } ? "failed" : "success";
        }

        if (result == "failed" && output.Get("retry_scheduled") is WireBool { Value: true })
        {
            // Not the end of it: the standard calls a failure that will be tried again "retrying".
            result = "retrying";
        }

        var trigger = output.Get("trigger") is WireString rawTrigger && ActivityClassifier.Triggers.Contains(rawTrigger.Value) ? rawTrigger.Value : "worker";
        string? nextAction = null;
        if (result == "failed" && !(Truthy(output.Get("pass_through_queued")) || Truthy(output.Get("reject_queued"))))
        {
            // The reason stays in the detail; an action is something the operator can do.
            nextAction = "Open this file on the History screen for what went wrong, fix the cause, then use Try again there.";
        }

        var envelope = OperatorMessages.ActivityDetailEnvelope(
            module: "processing",
            action: "remux",
            trigger: trigger,
            result: result,
            mediaScope: output.Get("media_scope") is WireString scope ? scope.Value : null,
            counts:
            [
                new("audio_removed", ListLength(output.Get("removed_audio"))),
                new("subtitles_removed", ListLength(output.Get("removed_subtitles"))),
            ],
            userMessage: ActivityTitle(output),
            nextAction: nextAction);
        foreach (var (key, item) in envelope.Items)
        {
            output.Set(key, item);
        }

        if (output.Get("ffmpeg_argv") is WireArray { Items.Count: > MaxArgvForActivity } argv)
        {
            var clipped = new WireArray(argv.Items.Take(MaxArgvForActivity));
            clipped.Items.Add(new WireString("…(truncated for activity log)"));
            output.Set("ffmpeg_argv", clipped);
            output.Set("ffmpeg_argv_truncated", true);
        }

        return output;
    }

    /// <summary>The clipped payload as ASCII JSON, cut at 10,000 characters.</summary>
    public static string ActivityDetail(WireObject payload, int maxChars = 10_000) =>
        WireStrings.Slice(WireJsonWriter.Dumps(ClipForActivity(payload), WireJsonFormat.Compact), maxChars);

    /// <summary>True when the value is present and not null, false, zero or empty.</summary>
    public static bool Truthy(WireValue? value) => value is not null && value.IsTruthy;

    /// <summary>The length of a list, string or object value; 0 for anything else, including a missing key.</summary>
    private static long ListLength(WireValue? value) => value switch
    {
        WireArray list => list.Items.Count,
        WireString text => WireStrings.Length(text.Value),
        WireObject dict => dict.Count,
        _ => 0,
    };
}
