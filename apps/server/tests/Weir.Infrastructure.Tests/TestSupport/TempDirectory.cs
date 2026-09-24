namespace Weir.Infrastructure.Tests;

/// <summary>
/// A temporary directory removed after the test. A caller that opened a <c>SqliteDatabase</c> inside it
/// must release that database's own pooled connections itself (<c>SqliteDatabase.ClearPool()</c>) before
/// this runs; deleting does not clear every pool in the process, because that would race with any other
/// test's connections still open at the same time.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "weir-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Join(params string[] parts) => System.IO.Path.Join([Path, .. parts]);

    /// <summary>
    /// One immediate retry (a file handle closing on another thread is usually already gone by the time
    /// <see cref="Dispose"/> runs); a folder that still won't delete is reported rather than left as a
    /// silent leak under the OS temp directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RetryOnceOrReport();
        }
    }

    private void RetryOnceOrReport()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Leaked test temp directory (still in use after one retry): {Path} ({exception.GetType().Name}: {exception.Message})");
        }
    }
}
