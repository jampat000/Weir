using Microsoft.Data.Sqlite;

namespace Weir.Contract.Tests.Harness;

/// <summary>
/// The server's SQLite file, open while the server is stopped. The contract is the HTTP API and the schema, so a
/// test may write rows the API cannot create, or read rows the API never shows, but only while nothing else holds
/// the file. Disposing closes the file and starts the server again (unless it was stopped with restart: false).
/// </summary>
public sealed class StoppedDatabase : IAsyncDisposable
{
    private static readonly string[] FileSuffixes = ["", "-wal", "-shm"];
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<Task> _restart;

    private StoppedDatabase(string path, Func<Task> restart)
    {
        _restart = restart;
        // No pooling: a pooled connection would keep the file open after Dispose and block the server.
        Connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true,
        }.ToString());
        Connection.Open();
    }

    public SqliteConnection Connection { get; }

    /// <summary>Opens the file once the operating system has released every handle the stopped server held on it.</summary>
    public static async Task<StoppedDatabase> OpenAsync(string path, Func<Task> restart)
    {
        await WaitForReleaseAsync(path);
        return new StoppedDatabase(path, restart);
    }

    /// <summary>Waits until no process holds the database files (the SQLite file and its write-ahead and shared-memory files).</summary>
    public static Task WaitForReleaseAsync(string path) =>
        Poll.UntilAsync(() => Task.FromResult(FilesAreFree(path)), "the stopped server to release its database files", ReleaseTimeout);

    public async ValueTask DisposeAsync()
    {
        await Connection.DisposeAsync();
        await _restart();
    }

    private static bool FilesAreFree(string path)
    {
        foreach (var file in FileSuffixes.Select(suffix => path + suffix).Where(File.Exists))
        {
            try
            {
                using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return false;
            }
        }

        return true;
    }
}
