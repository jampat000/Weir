namespace Weir.Core.Processing;

/// <summary>How a list of files is ordered.</summary>
public enum ProcessingFileSort
{
    /// <summary>When a scan last saw the file: the order a list has when it is not asked for another.</summary>
    LastSeen,

    /// <summary>The file's path, ignoring the case of its letters.</summary>
    File,

    /// <summary>
    /// What the file's status means (<see cref="ProcessingFileMeanings"/>), then the status itself. A cleaned copy still waiting
    /// for its media manager to import it means "to do", whatever its status.
    /// </summary>
    Status,

    /// <summary>When the file last changed.</summary>
    When,
}

/// <summary>The wire names of <see cref="ProcessingFileSort"/>, as a <c>sort</c> query parameter spells them.</summary>
public static class ProcessingFileSorts
{
    public const string File = "file";
    public const string Status = "status";
    public const string When = "when";

    /// <summary>The sorts a request can ask for. <see cref="ProcessingFileSort.LastSeen"/> is what no request asking for a sort gets.</summary>
    public static readonly IReadOnlyList<string> All = [File, Status, When];

    /// <summary>What a cursor made under <see cref="ProcessingFileSort.LastSeen"/> calls it.</summary>
    private const string LastSeen = "last_seen";

    public static string NameOf(ProcessingFileSort sort) => sort switch
    {
        ProcessingFileSort.File => File,
        ProcessingFileSort.Status => Status,
        ProcessingFileSort.When => When,
        _ => LastSeen,
    };

    /// <summary>The sort a wire name stands for, or false when it is not one of <see cref="All"/>.</summary>
    public static bool TryParse(string? name, out ProcessingFileSort sort)
    {
        sort = name switch
        {
            File => ProcessingFileSort.File,
            Status => ProcessingFileSort.Status,
            When => ProcessingFileSort.When,
            _ => ProcessingFileSort.LastSeen,
        };
        return name is File or Status or When;
    }
}
