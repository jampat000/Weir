using System.Text.Json.Nodes;

namespace Weir.Contract.Tests.Harness;

/// <summary>Bodies for the Processing libraries routes that more than one area builds the same way.</summary>
public static class LibraryBodies
{
    // What a library read returns that the save route does not accept (it forbids extra fields).
    private static readonly HashSet<string> ReadOnlyFields =
    [
        "id",
        "display_order",
        "manager_coverage",
        "manager_coverage_detail",
        "discovered_from_connection_id",
        "discovered_library_key",
        "folders_synced_from_connection_id",
        "effective_max_concurrent_files",
        "active_job_count",
        "next_look_at",
        "periodic_scan",
        "next_scan_at",
        "updated_at",
    ];

    /// <summary>
    /// A save is the whole library, so this is the body that changes nothing: every field of <paramref name="library"/>
    /// (as a read returned it) that the save route accepts. Apply the changes to the result.
    /// </summary>
    public static JsonObject Unchanged(JsonObject library)
    {
        var body = new JsonObject();
        foreach (var (name, value) in library.Where(field => !ReadOnlyFields.Contains(field.Key)))
        {
            body[name] = value?.DeepClone();
        }

        return body;
    }
}
