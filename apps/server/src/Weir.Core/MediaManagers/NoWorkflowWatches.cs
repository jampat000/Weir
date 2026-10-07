using Weir.Core.Json;
using Weir.Core.Observability;

namespace Weir.Core.MediaManagers;

/// <summary>A workflow as a refused hand-off lists it: its name, its media type and the folder it watches ("" when none is set).</summary>
public sealed record WorkflowFolder(string Name, string MediaType, string WatchedFolder);

/// <summary>
/// The refusal for a hand-off whose file lies in no workflow's watched folder. It names the file by its last path
/// segment only, as <see cref="HandoffPaths"/> does, but lists Weir's own workflows and folders: it is only raised
/// once the caller has proved the webhook secret, so unlike the path rules it may describe the layout of Weir's disk.
/// </summary>
public static class NoWorkflowWatches
{
    public static string Detail(IReadOnlyList<WorkflowFolder> workflows, string? filePath)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        var parts = WireStrings.Strip(filePath ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var lead = $"No Weir workflow watches the folder that {WireStrings.Repr(parts.Length > 0 ? parts[^1] : string.Empty)} is in. ";
        if (workflows.Count == 0)
        {
            return lead + "Weir has no workflows yet. Add one in Setup › Workflows, with the folder the download client finishes into as its watched folder, or add a path mapping in the media manager.";
        }

        return lead +
            $"Weir's workflows: {string.Join("; ", workflows.Select(Describe))}. " +
            "Set a workflow's watched folder (Setup › Workflows) to the folder the download client finishes into, or add a path mapping in the media manager.";
    }

    // "Movies (Movies)" would only say the same thing twice.
    private static string Describe(WorkflowFolder workflow)
    {
        var kind = OperatorMessages.MediaScopeLabel(workflow.MediaType) ?? workflow.MediaType;
        var name = string.Equals(workflow.Name, kind, StringComparison.OrdinalIgnoreCase) ? workflow.Name : $"{workflow.Name} ({kind})";
        return WireStrings.Strip(workflow.WatchedFolder).Length == 0
            ? $"{name} has no watched folder yet"
            : $"{name} watches '{workflow.WatchedFolder}'";
    }
}
