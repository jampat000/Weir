namespace Weir.Infrastructure.Processing;

/// <summary>
/// The steps a file passes through on its way from the watched folder to where it goes next, in order. The remux
/// pass names the one it is on in every live progress report, so screens draw the step Weir is on rather than
/// guess it.
/// </summary>
public static class PassStages
{
    /// <summary>Reading the file and confirming it can be read to the end.</summary>
    public const string Checking = "checking";

    /// <summary>Working out which tracks stay and which go.</summary>
    public const string Planning = "planning";

    /// <summary>Writing the cleaned-up copy, or copying the file unchanged.</summary>
    public const string Writing = "writing";

    /// <summary>The written file is being checked and its leftovers tidied.</summary>
    public const string Verifying = "verifying";

    /// <summary>The finished file is being handed back to where it goes next.</summary>
    public const string HandingBack = "handing_back";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { Checking, Planning, Writing, Verifying, HandingBack };

    /// <summary>The stage a report names, lower-cased, or <see langword="null"/> when it names none Weir knows.</summary>
    public static string? Normalize(string? stage)
    {
        var name = stage?.Trim().ToLowerInvariant();
        return name is not null && Known.Contains(name) ? name : null;
    }

    /// <summary>Whether the pass is still getting ready, before anything is written.</summary>
    internal static bool IsPreparation(string? stage) => stage is Checking or Planning;
}
