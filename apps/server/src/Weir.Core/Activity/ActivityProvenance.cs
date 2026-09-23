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
    public static PyDict JobProvenance(PyJson? payload)
    {
        var output = new PyDict();
        if (payload is not PyDict dict)
        {
            return output;
        }

        if (dict.Get("trigger") is PyStr trigger)
        {
            var normalized = PyStrings.Strip(trigger.Value).ToLowerInvariant();
            if (ActivityClassifier.Triggers.Contains(normalized))
            {
                output.Set("trigger", normalized);
            }
        }

        // A string or integer run_id only; a boolean is not a run id.
        if (dict.Get("run_id") is (PyStr or PyInt) and var runId && PyStrings.Strip(PyConvert.Str(runId)).Length > 0)
        {
            output.Set("run_id", runId);
        }

        return output;
    }

    /// <summary>The detail with the payload's provenance added, never overwriting what the detail says.</summary>
    public static PyDict WithProvenance(PyDict detail, PyJson? payload)
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
