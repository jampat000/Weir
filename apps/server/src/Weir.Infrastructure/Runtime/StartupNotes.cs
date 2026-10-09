using System.Text;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// What a starting server tells the Windows tray through two small files in Weir's data folder, which the tray reads
/// (<c>apps/tray/Weir.Tray/StartupNotes.cs</c> reads the same text; keep the two in step):
/// <list type="bullet">
/// <item><c>startup-progress.txt</c> exists while the server is doing something slow before it can answer, such as saving a copy of
/// the data before updating, and holds one sentence about it. The tray keeps waiting for a server that is alive and has said so.</item>
/// <item><c>startup-error.txt</c> holds why the server could not start, when it knows: a short headline on the first line and the
/// whole plain sentence after it. The tray shows it in its hover text and its balloon instead of a bare "couldn't start".</item>
/// </list>
/// Both are best effort: a start never fails because one could not be written. Each start removes the last one's.
/// </summary>
public static class StartupNotes
{
    public const string ProgressFileName = "startup-progress.txt";
    public const string ErrorFileName = "startup-error.txt";

    /// <summary>Removes what an earlier start left, so the tray never reads a reason that is not this start's.</summary>
    public static void ClearStale(string home)
    {
        Delete(home, ProgressFileName);
        Delete(home, ErrorFileName);
    }

    public static void WriteProgress(string home, string sentence) => Write(home, ProgressFileName, sentence);

    public static void ClearProgress(string home) => Delete(home, ProgressFileName);

    public static void WriteError(string home, string headline, string sentence) =>
        Write(home, ErrorFileName, $"{headline}\n{sentence}");

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
