using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Text;

namespace Weir.Core.MediaManagers;

/// <summary>
/// One file a hand-off covers and what its pass came to. <see cref="Result"/> is null until the file has a final result,
/// then one of <see cref="HandoffLedgerRules.Completed"/>, <see cref="HandoffLedgerRules.PassedThrough"/>,
/// <see cref="HandoffLedgerRules.Failed"/> or <see cref="HandoffLedgerRules.Cancelled"/>.
/// <see cref="OutputFile"/> is the copy Weir wrote, as Weir sees it.
/// </summary>
public sealed record HandoffTarget(string RelativePath, string? Result, string? OutputFile, string? Message)
{
    public bool Delivered => Result is HandoffLedgerRules.Completed or HandoffLedgerRules.PassedThrough;
}

/// <summary>The <c>outputFiles</c> list a report and the hand-off status carry, and how the ledger stores it.</summary>
public static class HandoffOutputFiles
{
    public static PyList ToJson(IEnumerable<string> files) => new(files.Select(file => (PyJson)new PyStr(file)));

    public static PyJson ToJsonOrNull(IEnumerable<string>? files) => files is null ? PyNull.Instance : ToJson(files);

    public static string Serialize(IEnumerable<string> files) => PyJsonWriter.Dumps(ToJson(files), PyJsonFormat.Compact);

    /// <summary>The stored list, or null when none is stored or it cannot be read.</summary>
    public static IReadOnlyList<string>? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return PyJsonParser.Parse(json) is PyList list ? [.. list.Items.OfType<PyStr>().Select(item => item.Value)] : null;
        }
        catch (PyJsonDecodeException)
        {
            return null;
        }
    }
}

/// <summary>
/// The single report a hand-off of several files gets once every one of them has a final result. The manager is told
/// about all of them at once: <c>outputPath</c> is the folder they were handed back in and <c>outputFiles</c> lists each
/// one, so it never imports the first file to finish and takes the hand-off as done.
/// </summary>
public static class FolderHandoffReports
{
    /// <summary>What one pass's result means for its file: delivered, handed back unchanged, or failed.</summary>
    public static string TargetResult(PyDict result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!CompletionReports.IsSucceeded(result))
        {
            return HandoffLedgerRules.Failed;
        }

        return result.Get("passed_through_after_failure") is PyBool { Value: true } ? HandoffLedgerRules.PassedThrough : HandoffLedgerRules.Completed;
    }

    /// <summary>The ledger state for the whole hand-off: as far along as its least finished file.</summary>
    public static string State(IReadOnlyCollection<HandoffTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return HandoffLedgerRules.Combine([.. targets.Select(target => target.Result ?? HandoffLedgerRules.Queued)]);
    }

    /// <summary>
    /// The report body. Every file delivered is <c>completed</c>; any file that failed makes it <c>failed</c>, and the
    /// message says how many succeeded and what went wrong with the rest, while <c>outputFiles</c> still lists the ones
    /// that were delivered.
    /// </summary>
    public static PyDict BuildBody(HandoffOrigin origin, IReadOnlyList<HandoffTarget> targets, string? outputFolder, IReadOnlyList<string> outputFiles)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(outputFiles);
        var failed = targets.Where(target => target.Result == HandoffLedgerRules.Failed).ToList();
        var body = CompletionReports.ReportHeader(origin, failed.Count == 0 ? "completed" : "failed");
        if (failed.Count == 0)
        {
            if (!string.IsNullOrEmpty(outputFolder))
            {
                body.Set("outputPath", outputFolder);
            }

            body.Set("message", SuccessMessage(targets));
        }
        else
        {
            body.Set("message", FailureMessage(targets, failed))
                .Set("disposition", "held")
                .Set("sourceRemoved", false);
        }

        return body.Set("outputFiles", HandoffOutputFiles.ToJson(outputFiles));
    }

    private static string SuccessMessage(IReadOnlyList<HandoffTarget> targets)
    {
        var delivered = targets.Count(target => target.Delivered);
        var passedThrough = targets.Count(target => target.Result == HandoffLedgerRules.PassedThrough);
        var cancelled = targets.Count(target => target.Result == HandoffLedgerRules.Cancelled);
        var message = $"Weir finished {Plural.Of(delivered, "file")}, ready to import from the output folder.";
        if (passedThrough > 0)
        {
            message += $" It could not process {Plural.Of(passedThrough, "file")}, so it handed {Plural.Noun(passedThrough, "that one", "those")} back unchanged.";
        }

        if (cancelled > 0)
        {
            message += $" {Plural.Of(cancelled, "file")} {Plural.Noun(cancelled, "was", "were")} cancelled in Weir.";
        }

        return message;
    }

    private static string FailureMessage(IReadOnlyList<HandoffTarget> targets, List<HandoffTarget> failed)
    {
        var delivered = targets.Count(target => target.Delivered);
        var reasons = failed.Select(target => $"{MediaPathNames.Name(target.RelativePath, windows: false)}: {PyStrings.Strip(target.Message ?? string.Empty).TrimEnd('.')}");
        return $"Weir finished {delivered.ToString(System.Globalization.CultureInfo.InvariantCulture)} of {Plural.Of(targets.Count, "file")}. " +
               $"It could not process {Plural.Of(failed.Count, "file")}: {string.Join("; ", reasons)}.";
    }
}
