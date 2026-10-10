namespace Weir.Tray;

/// <summary>Why the server could not start, in the server's own plain words.</summary>
/// <param name="Headline">A few words, short enough for the icon's hover text.</param>
/// <param name="Detail">The whole sentence, with what to do about it, for a balloon.</param>
sealed record StartupError(string Headline, string Detail);

/// <summary>
/// What a starting server says through two small files in the runtime home (<c>StartupNotes</c> in the server's
/// <c>Weir.Infrastructure/Runtime</c> writes the same text; keep the two in step): <c>startup-progress.txt</c> exists while the
/// server does something slow before it can answer, such as saving a copy of the data before an update, and
/// <c>startup-error.txt</c> says why it could not start. A file is the server's only when it was written after the server
/// process began: the last start's is not this one's.
/// </summary>
static class StartupNotes
{
    internal const string ProgressFileName = "startup-progress.txt";
    internal const string ErrorFileName = "startup-error.txt";

    /// <summary>
    /// How old the server's progress note may be and still be believed. The server touches it about every ten seconds while its
    /// work runs, so a healthy copy of any length keeps the tray waiting and a hung one shows as a problem within this time.
    /// </summary>
    internal static readonly TimeSpan ProgressFreshFor = TimeSpan.FromMinutes(2);

    /// <summary>Why the server that began at <paramref name="serverStartedUtc"/> could not start, or null when it said nothing.</summary>
    internal static StartupError? ReadError(string runtimeHome, DateTime? serverStartedUtc)
    {
        if (ReadFresh(Path.Combine(runtimeHome, ErrorFileName), serverStartedUtc) is not { } text)
        {
            return null;
        }
        var lines = text.Split('\n', 2, StringSplitOptions.TrimEntries);
        var headline = lines[0];
        var detail = lines.Length > 1 && lines[1].Length > 0 ? lines[1] : headline;
        return headline.Length == 0 ? null : new StartupError(headline, detail);
    }

    /// <summary>Whether the server that began at <paramref name="serverStartedUtc"/> has said it is busy, and recently.</summary>
    internal static bool IsBusy(string runtimeHome, DateTime? serverStartedUtc, DateTime nowUtc)
    {
        var path = Path.Combine(runtimeHome, ProgressFileName);
        return ReadFresh(path, serverStartedUtc) is not null && nowUtc - File.GetLastWriteTimeUtc(path) < ProgressFreshFor;
    }

    // Written no earlier than the process began: a file from an earlier process is never this one's.
    private static string? ReadFresh(string path, DateTime? serverStartedUtc)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            if (serverStartedUtc is { } started && File.GetLastWriteTimeUtc(path) < started)
            {
                return null;
            }
            return File.ReadAllText(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
