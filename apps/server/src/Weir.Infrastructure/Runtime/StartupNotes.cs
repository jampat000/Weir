using System.Text;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// What a starting server tells the Windows tray through two small files in Weir's data folder, which the tray reads
/// (<c>apps/tray/Weir.Tray/StartupNotes.cs</c> reads the same text; keep the two in step):
/// <list type="bullet">
/// <item><c>startup-progress.txt</c> exists while the server is doing something slow before it can answer, such as saving a copy of
/// the data before updating, and holds one sentence about it. It is touched every <see cref="ProgressInterval"/> meanwhile, so the
/// tray keeps waiting for a server whose note is recent and stops believing one whose note has gone quiet.</item>
/// <item><c>startup-error.txt</c> holds why the server could not start, when it knows: a short headline on the first line and the
/// whole plain sentence after it. The tray shows it in its hover text and its balloon instead of a bare "couldn't start".</item>
/// </list>
/// Both are best effort: a start never fails because one could not be written. Each start removes the last one's.
/// </summary>
public static class StartupNotes
{
    public const string ProgressFileName = "startup-progress.txt";
    public const string ErrorFileName = "startup-error.txt";

    /// <summary>How often the progress note is touched while the slow work runs; the tray takes a note a couple of minutes old as stale.</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(10);

    /// <summary>Removes what an earlier start left, so the tray never reads a reason that is not this start's.</summary>
    public static void ClearStale(string home)
    {
        Delete(home, ProgressFileName);
        Delete(home, ErrorFileName);
    }

    public static void WriteProgress(string home, string sentence) => Write(home, ProgressFileName, sentence);

    /// <summary>
    /// Says <paramref name="sentence"/> and keeps saying it, by touching the note every <see cref="ProgressInterval"/> on a timer
    /// of its own (the slow work holds its own thread), until the result is disposed, which removes the note.
    /// </summary>
    public static IDisposable BeginProgress(string home, string sentence, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        WriteProgress(home, sentence);
        var timer = time.CreateTimer(_ => Touch(home, time), null, ProgressInterval, ProgressInterval);
        return new Progress(home, timer);
    }

    public static void ClearProgress(string home) => Delete(home, ProgressFileName);

    public static void WriteError(string home, string headline, string sentence) =>
        Write(home, ErrorFileName, $"{headline}\n{sentence}");

    private static void Touch(string home, TimeProvider time)
    {
        try
        {
            var path = Path.Join(home, ProgressFileName);
            if (File.Exists(path))
            {
                File.SetLastWriteTimeUtc(path, time.GetUtcNow().UtcDateTime);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The next touch tries again.
        }
    }

    private sealed class Progress(string home, ITimer timer) : IDisposable
    {
        public void Dispose()
        {
            timer.Dispose();
            ClearProgress(home);
        }
    }

    private static void Write(string home, string fileName, string text)
    {
        try
        {
            AtomicFileWriter.Replace(home, fileName, Encoding.UTF8.GetBytes(text));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The tray shows its general message instead.
        }
    }

    private static void Delete(string home, string fileName)
    {
        try
        {
            File.Delete(Path.Join(home, fileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Read as the last start's, which the tray tells from this one's by when it was written.
        }
    }
}
