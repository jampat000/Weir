using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>One filesystem/database finding.</summary>
public sealed record ReconciliationIssue(
    string Kind,
    string Module,
    string Severity,
    string Message,
    string? Path = null,
    string? DbTable = null,
    long? DbId = null,
    string? RepairAction = null,
    bool RequiresConfirmation = false)
{
    public WireObject AsDict() => new WireObject()
        .Set("kind", Kind)
        .Set("module", Module)
        .Set("severity", Severity)
        .Set("message", Message)
        .Set("path", Path)
        .Set("db_table", DbTable)
        .Set("db_id", DbId)
        .Set("repair_action", RepairAction)
        .Set("requires_confirmation", RequiresConfirmation);
}

/// <summary>The reconciliation rules that do not touch the database or filesystem.</summary>
public static class ReconciliationRules
{
    public const string RemoveTempArtifactAction = "remove_processing_temp_artifact";
    public const int MaxIssuesPerCategory = 200;

    /// <summary>Suffixes that mark a leftover temporary file.</summary>
    public static readonly IReadOnlyList<string> TempArtifactSuffixes = [".partial", ".part", ".tmp", ".link"];

    /// <summary>Whether a file name is a leftover temporary file: hidden, or one of <see cref="TempArtifactSuffixes"/>.</summary>
    public static bool IsTempArtifactName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var lower = name.ToLowerInvariant();
        return lower.StartsWith('.') || TempArtifactSuffixes.Any(suffix => lower.EndsWith(suffix, StringComparison.Ordinal));
    }

    /// <summary>The reconciliation report's JSON shape.</summary>
    public static WireObject Report(IReadOnlyList<ReconciliationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        return new WireObject()
            .Set("ok", issues.Count == 0)
            .Set("issue_count", issues.Count)
            .Set("issues", new WireArray(issues.Select(issue => (WireValue)issue.AsDict())))
            .Set("repair_actions", new WireArray(issues
                .Select(issue => issue.RepairAction)
                .Where(action => !string.IsNullOrEmpty(action))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(action => (WireValue)new WireString(action!))));
    }
}
