using Weir.Core.Json;

namespace Weir.Core.Jobs;

/// <summary>
/// Plain-language status summaries for persisted background jobs. Job rows retain the technical error
/// for diagnostics; this is the one translation every inspection screen shares.
/// </summary>
public static class OperatorJobStatus
{
    /// <summary>
    /// Display names for module keys whose plain capitalization would not read as a person expects.
    /// The "processing" module key is the stored identifier the caller passes; the app that runs it is
    /// called Weir.
    /// </summary>
    private static readonly Dictionary<string, string> ModuleDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["processing"] = "Weir",
    };

    private static string Clean(string? raw, int limit = 1200)
    {
        var joined = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return joined.Length > limit ? joined[..limit] : joined;
    }

    private static string? FileName(WireObject payload)
    {
        if (!payload.TryGetValue("relative_media_path", out var value) || value is not WireString s || s.Value.Trim().Length == 0)
        {
            return null;
        }

        var normalized = s.Value.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    /// <summary>A job's status for an operator: the API's <c>operator_message</c>, <c>next_action</c> and <c>technical_detail</c>.</summary>
    public static (string Message, string NextAction, string? TechnicalDetail) Build(string module, string jobKind, string status, string? lastError, string? payloadJson)
    {
        _ = jobKind;
        var label = ModuleDisplayNames.TryGetValue(module, out var displayName)
            ? displayName
            : module.Length > 0 ? char.ToUpperInvariant(module[0]) + module[1..] : module;
        var payload = ParsePayload(payloadJson);
        var fileName = FileName(payload);
        var subject = fileName is not null ? $" for {fileName}" : string.Empty;
        var error = Clean(lastError, 10_000);
        var lower = error.ToLowerInvariant();
        var technical = error.Length > 0 ? error : null;

        switch (status)
        {
            case "pending":
                return ($"Queued{subject}.", "Nothing to do. It starts when a worker is free.", technical);
            case "leased":
                return ($"Running{subject}.", "Nothing to do. If it stays here too long, open the job record.", technical);
            case "completed":
                return ($"Finished{subject}.", "Nothing to do. Open the processing record for the outcome.", technical);
            case "cancelled":
                return ($"Cancelled before it started{subject}.", "Nothing to do. To process the file anyway, start it again from Activity.", technical);
            case "handler_ok_finalize_failed":
                return ($"The work finished{subject}, but its result could not be saved.", "Use Recover result below. The media work won't run again.", technical);
        }

        if (lower.Contains("database is locked", StringComparison.Ordinal) || lower.Contains("database table is locked", StringComparison.Ordinal))
        {
            return string.Equals(module, "processing", StringComparison.OrdinalIgnoreCase)
                ? ($"Weir could not save the result while another local operation was using the database{subject}.",
                   "Try the file again. If it repeats, set ‘Files at once’ to 1, let the current work finish, and retry.", technical)
                : ($"Weir could not save the {label} result because another local operation was using the database.",
                   "Try the job again after the current local work finishes.", technical);
        }

        if (lower.Contains("not a supported processing media", StringComparison.Ordinal) || lower.Contains("unsupported processing", StringComparison.Ordinal) || lower.Contains("processing does not process", StringComparison.Ordinal) || lower.Contains("weir does not process", StringComparison.Ordinal))
        {
            return ($"This file is not a supported media file for this pass{subject}.", "Choose a supported video file or update the workflow’s media types, then start it again.", technical);
        }

        if (lower.Contains("could not find this file", StringComparison.Ordinal) || lower.Contains("file not found", StringComparison.Ordinal) || lower.Contains("no such file", StringComparison.Ordinal))
        {
            return ($"Weir could not find this file under the saved watched folder{subject}.", "Check the workflow’s folder or restore the file, then use Start again.", technical);
        }

        if (lower.Contains("ffprobe failed", StringComparison.Ordinal) || lower.Contains("could not read this media", StringComparison.Ordinal)
            || lower.Contains("couldn't read this file", StringComparison.Ordinal) || lower.Contains("can't read this file", StringComparison.Ordinal)
            || lower.Contains("media tools couldn't finish", StringComparison.Ordinal))
        {
            return ($"Weir could not read this media file{subject}.", "Check that the file is complete and playable, then use Try again.", technical);
        }

        if (lower.Contains("legacy processing dry_run", StringComparison.Ordinal) || lower.Contains("legacy weir dry_run", StringComparison.Ordinal))
        {
            return ($"This job was created with an older processing mode{subject}.", "Remove the old entry from Activity, then let the next scan create a current job.", technical);
        }

        if (lower.Contains("modified too recently", StringComparison.Ordinal)
            || lower.Contains("changed too recently", StringComparison.Ordinal)
            || lower.Contains("still being written", StringComparison.Ordinal))
        {
            return ($"Weir is waiting for this file to finish changing{subject}.", "Wait for the copy or import to finish, then use Check again from Activity.", technical);
        }

        if (lower.Contains("no retainable audio", StringComparison.Ordinal))
        {
            return ($"Weir could not build a safe audio plan for this file{subject}.", "Check the file’s audio tracks and saved audio rules, then use Try again.", technical);
        }

        if (status is "failed" or "error")
        {
            return ($"Couldn't finish this job{subject}.", "Read the error below, fix the cause, then use Try again in Activity.", technical);
        }

        return ($"Needs a review{subject}.", "Open it in System › Logs to see why.", technical);
    }

    private static WireObject ParsePayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new WireObject();
        }

        try
        {
            return WireJsonParser.Parse(raw) as WireObject ?? new WireObject();
        }
        catch (WireJsonDecodeException)
        {
            return new WireObject();
        }
    }
}
