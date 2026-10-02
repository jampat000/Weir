using Weir.Core.Json;

namespace Weir.Core.Logs;

/// <summary>
/// Where a row of System › Logs came from. The order is the tie-break between rows with the same time, so a page boundary
/// never depends on which source answered first.
/// </summary>
public enum SystemLogSource
{
    /// <summary>A line of the server log file.</summary>
    Server = 0,

    /// <summary>A background job's row.</summary>
    Job = 1,

    /// <summary>An Activity event about Weir itself.</summary>
    Event = 2,
}

/// <summary>The wire names of <see cref="SystemLogSource"/>.</summary>
public static class SystemLogSources
{
    public const string Event = "event";
    public const string Job = "job";
    public const string Server = "server";

    public static readonly IReadOnlyList<SystemLogSource> All = [SystemLogSource.Event, SystemLogSource.Job, SystemLogSource.Server];

    public static string NameOf(SystemLogSource source) => source switch
    {
        SystemLogSource.Event => Event,
        SystemLogSource.Job => Job,
        _ => Server,
    };

    public static bool TryParse(string? name, out SystemLogSource source)
    {
        switch (name)
        {
            case Event:
                source = SystemLogSource.Event;
                return true;
            case Job:
                source = SystemLogSource.Job;
                return true;
            case Server:
                source = SystemLogSource.Server;
                return true;
            default:
                source = default;
                return false;
        }
    }
}

/// <summary>How a row went, in the four colours the screen draws it with.</summary>
public static class SystemLogLevels
{
    public const string Error = "error";
    public const string Warning = "warning";
    public const string Info = "info";
    public const string Success = "success";

    public static readonly IReadOnlyList<string> All = [Error, Warning, Info, Success];
}

/// <summary>What a row is about, in the words the category picker uses. Every row has exactly one.</summary>
public static class SystemLogCategories
{
    public const string Processing = "processing";
    public const string Scans = "scans";
    public const string Cleanup = "cleanup";
    public const string Library = "library";
    public const string Connections = "connections";
    public const string Backups = "backups";
    public const string SignIn = "sign_in";
    public const string Updates = "updates";
    public const string Weir = "weir";

    public static readonly IReadOnlyList<string> All =
        [Processing, Scans, Cleanup, Library, Connections, Backups, SignIn, Updates, Weir];
}

/// <summary>
/// One thing that happened, whichever source recorded it. <paramref name="Record"/> is that source's own record
/// (the Activity event, the job or the log line) in the shape its own endpoint returns it.
/// </summary>
/// <param name="Source">Where the row came from.</param>
/// <param name="Key">The row's number in its source: the event's or job's id, or the log line's place in the file.</param>
/// <param name="At">When it happened (UTC, whole milliseconds).</param>
/// <param name="Level">One of <see cref="SystemLogLevels"/>.</param>
/// <param name="Category">One of <see cref="SystemLogCategories"/>.</param>
/// <param name="WorkflowId">The workflow (library) it belongs to, when it has one.</param>
/// <param name="Title">One line in plain words.</param>
/// <param name="Detail">A quieter second line, when there is something to add.</param>
/// <param name="Record">The source's own record.</param>
public sealed record SystemLogRow(
    SystemLogSource Source,
    long Key,
    DateTimeOffset At,
    string Level,
    string Category,
    long? WorkflowId,
    string Title,
    string? Detail,
    WireObject Record)
{
    /// <summary>The row's place in the newest-first order.</summary>
    public SystemLogPosition Position => new(At, Source, Key);

    /// <summary>The row's id on the wire, such as <c>event:41</c>.</summary>
    public string Id => $"{SystemLogSources.NameOf(Source)}:{Key}";
}

/// <summary>
/// Where a row sits in the log, which is also what a cursor remembers: later time first, then the source, then the number
/// in the source, all descending.
/// </summary>
public readonly record struct SystemLogPosition(DateTimeOffset At, SystemLogSource Source, long Key)
{
    /// <summary>Orders positions newest first: the later time, then the later source, then the higher number.</summary>
    public static readonly IComparer<SystemLogPosition> NewestFirst = Comparer<SystemLogPosition>.Create(Compare);

    /// <summary>Whether this position comes after <paramref name="cursor"/> in the newest-first order.</summary>
    public bool IsAfter(SystemLogPosition cursor) => Compare(this, cursor) > 0;

    private static int Compare(SystemLogPosition first, SystemLogPosition second)
    {
        var byTime = second.At.UtcTicks.CompareTo(first.At.UtcTicks);
        if (byTime != 0)
        {
            return byTime;
        }

        var bySource = ((int)second.Source).CompareTo((int)first.Source);
        return bySource != 0 ? bySource : second.Key.CompareTo(first.Key);
    }
}

/// <summary>The filters of one read of the log. Every list is empty when the filter is not set.</summary>
public sealed record SystemLogFilter
{
    public IReadOnlyList<SystemLogSource> Sources { get; init; } = [];

    public IReadOnlyList<string> Levels { get; init; } = [];

    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>The workflow's id; leaves out the server log, whose lines belong to no workflow.</summary>
    public long? WorkflowId { get; init; }

    /// <summary>Words to find in a row's text.</summary>
    public string? Text { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    /// <summary>One job, and the rows that mention it: events naming it and server lines written while it ran.</summary>
    public long? JobId { get; init; }

    /// <summary>An Activity event type. Only events have one, so setting it leaves out the other sources.</summary>
    public string? EventType { get; init; }

    /// <summary>An Activity event's result. Only events have one.</summary>
    public string? Result { get; init; }

    /// <summary>Why an Activity event happened. Only events have one.</summary>
    public string? Trigger { get; init; }

    /// <summary>Job statuses. Only jobs have one.</summary>
    public IReadOnlyList<string> JobStatuses { get; init; } = [];

    /// <summary>Only server lines that carry an exception (true) or none (false). Only the server log has them.</summary>
    public bool? HasException { get; init; }

    /// <summary>Whether <paramref name="source"/> can hold a row this filter lets through, ignoring the source filter itself.</summary>
    public bool CanMatch(SystemLogSource source) => source switch
    {
        SystemLogSource.Event => JobStatuses.Count == 0 && HasException is null,
        SystemLogSource.Job => EventType is null && Result is null && Trigger is null && HasException is null,
        _ => WorkflowId is null && EventType is null && Result is null && Trigger is null && JobStatuses.Count == 0,
    };

    /// <summary>Whether the filter names <paramref name="source"/>, or names no source at all.</summary>
    public bool Selects(SystemLogSource source) => Sources.Count == 0 || Sources.Contains(source);
}

/// <summary>How many rows each choice of a filter would show, counted with every other filter applied (but not its own).</summary>
public sealed record SystemLogCounts(
    IReadOnlyDictionary<string, long> BySource,
    IReadOnlyDictionary<string, long> ByLevel,
    IReadOnlyDictionary<string, long> ByCategory);

/// <summary>One page of the log, newest first.</summary>
public sealed record SystemLogPage(
    IReadOnlyList<SystemLogRow> Rows,
    string? NextCursor,
    long Total,
    SystemLogCounts Counts);
