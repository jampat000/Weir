using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// What a workflow's links to media managers (<c>library_manager_links</c>) mean for the original downloads and for how it
/// is fed. Two rules, both decided from the links as they are right now and never from a stored setting:
/// a linked workflow's originals belong to the download client and the manager, so Weir never removes or moves them; and a
/// workflow linked to Deluno is processed only from Deluno's hand-off, so Weir's own watched-folder scan never queues work for it.
/// </summary>
/// <param name="Kinds">The kind of each linked connection (<see cref="MediaManagerKinds"/>), in link order.</param>
public sealed record WorkflowManagerLinks(IReadOnlyList<string> Kinds)
{
    private const string Deluno = "deluno";

    /// <summary>A workflow with no media manager linked: "Weir only".</summary>
    public static readonly WorkflowManagerLinks None = new([]);

    /// <summary>Linked to at least one media manager of any kind (Deluno, Sonarr, Radarr or another).</summary>
    public bool IsLinked => Kinds.Count > 0;

    /// <summary>The original download belongs to the download client and the manager: Weir leaves it, its sidecars and its folder alone.</summary>
    public bool KeepsOriginals => IsLinked;

    /// <summary>A linked Deluno hands each finished download to Weir, so Weir's own scan has nothing to add and must not queue work.</summary>
    public bool HandedOffByManager => Kinds.Any(kind => IsKind(kind, Deluno));

    /// <summary>Why the original stays, naming the manager, e.g. "This workflow is linked to Deluno, so the original stays with your download client, which may still be seeding."</summary>
    public string KeptOriginalReason =>
        $"This workflow is linked to {Names(Kinds)}, so the original stays with your download client, which may still be seeding.";

    /// <summary>Why Weir does not scan this workflow's watched folder.</summary>
    public string ScanSkippedReason =>
        $"{Names(Kinds.Where(kind => IsKind(kind, Deluno)))} hands this workflow its downloads, so Weir does not scan its watched folder.";

    private static bool IsKind(string kind, string wanted) => string.Equals(WireStrings.Strip(kind), wanted, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Deluno", "Deluno and Sonarr": each product once. A manager with no product name of its own reads "your media manager".</summary>
    private static string Names(IEnumerable<string> kinds)
    {
        var names = kinds
            .Select(kind => ManagerKindProfiles.ForKind(kind) is { Kind: not MediaManagerKinds.Native } profile ? MediaManagerKinds.ProductLabel(profile.Kind) : "your media manager")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}
