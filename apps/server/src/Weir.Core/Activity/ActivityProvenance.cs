using Weir.Core.Json;

namespace Weir.Core.Activity;

/// <summary>
/// Why a piece of work is happening, carried from where it is queued to the Activity it writes
/// (#469). The trigger and run id ride on the job
/// payload; handlers copy them into their details and <see cref="ActivityClassifier"/> lifts them into columns.
/// </summary>
public static class ActivityProvenance
{
    /// <summary>The watched-folder scan's own trigger words in the shared vocabulary.</summary>
    public static readonly IReadOnlyDictionary<string, string> ScanTriggerToTrigger = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["manual"] = "manual",
        ["periodic"] = "scheduled",
        ["filesystem_event"] = "folder_change",
    };

    /// <summary><c>trigger</c> and <c>run_id</c> from a job payload, only when present and valid.</summary>
    public static WireObject JobProvenance(WireValue? payload)
    {
        var output = new WireObject();
        if (payload is not WireObject dict)
        {
            return output;
        }

        if (dict.Get("trigger") is WireString trigger)
        {
            var normalized = WireStrings.Strip(trigger.Value).ToLowerInvariant();
            if (ActivityClassifier.Triggers.Contains(normalized))
            {
                output.Set("trigger", normalized);
            }
        }

        // A string or integer run_id only; a boolean is not a run id.
        if (dict.Get("run_id") is (WireString or WireInteger) and var runId && WireStrings.Strip(WireConvert.Str(runId)).Length > 0)
        {
            output.Set("run_id", runId);
        }

        return output;
    }

    /// <summary>The detail with the payload's provenance added, never overwriting what the detail says.</summary>
    public static WireObject WithProvenance(WireObject detail, WireValue? payload)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var output = JobProvenance(payload);
        foreach (var (key, value) in detail.Items)
        {
            output.Set(key, value);
        }

        return output;
    }
}
