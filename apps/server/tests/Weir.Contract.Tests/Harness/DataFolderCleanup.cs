namespace Weir.Contract.Tests.Harness;

/// <summary>Removes a stopped server's data folder, waiting for the operating system to let go of the files the server held.</summary>
internal static class DataFolderCleanup
{
    private static readonly TimeSpan DeleteTimeout = TimeSpan.FromSeconds(30);

    public static async Task DeleteAsync(string home, string databasePath)
    {
        // The SQLite files are the ones a killed server most often still holds for a moment; wait for those first, then
        // retry the delete in case something else (a killed child, a virus scanner) lets go a moment later.
        await StoppedDatabase.WaitForReleaseAsync(databasePath);
        await Poll.UntilAsync(() => Task.FromResult(TryDelete(home)), "the server's data folder to be deletable", DeleteTimeout);
    }

    private static bool TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
