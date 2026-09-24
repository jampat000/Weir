using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>Activity writes for a Pass 4 failure-cleanup sweep.</summary>
public static class ProcessingFailureCleanupActivity
{
    private static string Label(string mediaScope) => mediaScope == "tv" ? "TV" : "Movies";

    private static WireObject WithOutcome(WireObject detail, string result, string? trigger)
    {
        var copy = detail.Copy();
        if (copy.Get("result") is null)
        {
            copy.Set("result", result);
        }

        if (trigger is not null && copy.Get("trigger") is null)
        {
            copy.Set("trigger", trigger);
        }

        return copy;
    }

    public static Task RecordSweepStartedAsync(UnitOfWork uow, string mediaScope, WireObject detail, string? trigger)
    {
        var withOutcome = WithOutcome(detail, "running", trigger);
        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingFailureCleanupSweepCompleted, "processing",
            $"Cleanup started for {Label(mediaScope)}",
            WireStrings.Slice(WireJsonWriter.Dumps(withOutcome, WireJsonFormat.Compact), 10_000)));
    }

    public static Task RecordSweepCompletedAsync(UnitOfWork uow, string mediaScope, WireObject detail, string? trigger)
    {
        var label = Label(mediaScope);
        var status = detail.Get("cleanup_run_status") is WireString statusValue ? statusValue.Value : null;
        var (title, result) = status switch
        {
            "no_eligible_files" => ($"Cleanup checked {label}: no changes needed", "success"),
            "skipped" => ($"Cleanup skipped {label}", "skipped"),
            _ => ($"Cleaned up after failed files ({label})", "success"),
        };
        var withOutcome = WithOutcome(detail, result, trigger);
        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingFailureCleanupSweepCompleted, "processing", title,
            WireStrings.Slice(WireJsonWriter.Dumps(withOutcome, WireJsonFormat.Compact), 10_000)));
    }
}
