using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>The fixed-shape result dictionaries a pass returns, and the fingerprint guard behind them.</summary>
public sealed partial class RemuxPassRunner
{
    /// <summary>Throws when the source's fingerprint changed during the pass, so a staged output is never published from
    /// a source that moved underneath it.</summary>
    private static void AssertSourceUnchanged(string path, SourceFingerprint expected)
    {
        SourceFingerprint current;
        try
        {
            current = SourceFiles.Fingerprint(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new MediaCompletenessException(
                $"The source could not be rechecked after processing ({exception.Message}), so the staged output was not published.",
                exception);
        }

        if (current != expected)
        {
            throw new MediaCompletenessException(
                "The source changed while Weir was reading it. The staged output was discarded and Weir will wait " +
                "for the downloader or importer to finish.");
        }
    }

    /// <summary>The result of a pass that failed before execution.</summary>
    public static WireObject FailBefore(string relativeMediaPath, string reason, string? inspectedSourcePath = null, WireObject? extra = null)
    {
        var result = new WireObject()
            .Set("ok", false)
            .Set("outcome", RemuxPassOutcomes.FailedBeforeExecution)
            .Set("preflight_status", "failed")
            .Set("preflight_reason", reason)
            .Set("reason", reason)
            .Set("relative_media_path", relativeMediaPath);
        foreach (var (key, value) in extra?.Items ?? [])
        {
            result.Set(key, value);
        }

        if (!string.IsNullOrEmpty(inspectedSourcePath))
        {
            result.Set("inspected_source_path", inspectedSourcePath);
        }

        return result;
    }

    /// <summary><c>not_ready_kind</c> of a file that is only waiting out the minimum file age (#632).</summary>
    public const string MinimumAgeWait = "minimum_age";

    /// <summary><c>not_ready_kind</c> of a file Weir could not read from start to finish (#646).</summary>
    public const string UnreadableWait = "unreadable";

    /// <summary>The result of a source that is not ready yet: an expected wait, not a failure.</summary>
    public static WireObject SourceNotReady(string relativeMediaPath, string reason, string? inspectedSourcePath = null)
    {
        var result = new WireObject()
            .Set("ok", false)
            .Set("outcome", RemuxPassOutcomes.SourceNotReady)
            .Set("retryable_wait", true)
            .Set("preflight_status", "waiting")
            .Set("preflight_reason", reason)
            .Set("reason", reason)
            .Set("relative_media_path", relativeMediaPath);
        if (!string.IsNullOrEmpty(inspectedSourcePath))
        {
            result.Set("inspected_source_path", inspectedSourcePath);
        }

        return result;
    }

    /// <summary>The result of a pass a guardrail skipped.</summary>
    public static WireObject SkipGuardrail(string relativeMediaPath, string reason, string guardrail, string? inspectedSourcePath, WireObject extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        var result = new WireObject()
            .Set("ok", true)
            .Set("outcome", RemuxPassOutcomes.SkippedGuardrail)
            .Set("preflight_status", "skipped")
            .Set("preflight_reason", reason)
            .Set("reason", reason)
            .Set("guardrail", guardrail)
            .Set("relative_media_path", relativeMediaPath);
        foreach (var (key, value) in extra.Items)
        {
            result.Set(key, value);
        }

        if (inspectedSourcePath is not null)
        {
            result.Set("inspected_source_path", inspectedSourcePath);
        }

        return result;
    }

    private static WireArray StringList(IEnumerable<string> values) => new(values.Select(value => (WireValue)new WireString(value)));

    private static WireValue NullableFloat(double? value) => value is { } v ? new WireNumber(v) : WireNull.Instance;
}
