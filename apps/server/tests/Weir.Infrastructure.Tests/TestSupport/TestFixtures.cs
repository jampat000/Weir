namespace Weir.Infrastructure.Tests;

/// <summary>Folder links for tests of code that must never follow one.</summary>
internal static class FolderLinks
{
    /// <summary>A directory junction on Windows (no privilege needed) or a symbolic link elsewhere.</summary>
    public static void Create(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}

internal static class RepositoryPaths
{
    /// <summary>
    /// The checked-in schema reference, copied next to the test assembly: the schema and seed rows of the
    /// last Alembic head, which the baseline migration reproduces exactly. It is frozen; the migrations are
    /// the schema's only source.
    /// </summary>
    public static string AlembicHeadReference => Path.Join(AppContext.BaseDirectory, "schema", "alembic-head.sql");

    /// <summary>The repository root, found by walking up from the test assembly.</summary>
    public static string? RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (Directory.Exists(Path.Join(directory.FullName, "apps", "server")) &&
                    Directory.Exists(Path.Join(directory.FullName, "apps", "web")))
                {
                    return directory.FullName;
                }
            }

            return null;
        }
    }
}

/// <summary>
/// <see cref="Weir.Infrastructure.Tests.Jobs.ClaimConcurrencyTests"/> and
/// <see cref="Weir.Infrastructure.Tests.Jobs.LeaseRenewalTests"/> each block many thread-pool
/// threads at once (parallel SQLite claims behind a barrier, or a lease heartbeat driven by a shared
/// <c>FakeTimeProvider</c>). This collection keeps those two classes from running at the same time as each
/// other, so their thread-pool pressure doesn't compound.
/// </summary>
/// <remarks>
/// <c>DisableParallelization</c> only serializes the members of <em>this</em> collection against each
/// other; xUnit still runs every other (default, per-class) collection in the assembly concurrently with
/// it, so this alone does not give these tests exclusive use of the process. Cross-test flakiness (an
/// unrelated test's teardown calling the process-wide <c>SqliteConnection.ClearAllPools()</c> while these
/// tests had connections mid-transaction) is prevented at its source instead: every test releases only its own database's pool
/// (<see cref="Weir.Infrastructure.Sqlite.SqliteDatabase.ClearPool"/>), so no test's teardown can disturb
/// another test's live connection regardless of what runs alongside it.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialTestGroup
{
    public const string Name = "Serial: thread-pool heavy";
}

/// <summary>
/// Tests that time how long one lane's write waits while another lane's bulk job runs (#708), and the folder watcher's
/// real-<see cref="FileSystemWatcher"/> tests, which need an OS event delivered and processed inside a wall-clock window.
/// A bulk-job test here creates thousands of real files and hammers the database on a dedicated thread for as long as the
/// job runs; grouped with a real-timer test that would otherwise run at the same time, it starved the watcher's own
/// timers and event delivery on a loaded Windows CI runner (its debounce ticks and the reconcile loop share the process's
/// thread pool with everything else), which is exactly the interference <c>DisableParallelization</c> exists to remove
/// among a collection's own members.
/// </summary>
/// <remarks>
/// As with <see cref="SerialTestGroup"/>, <c>DisableParallelization</c> only serializes the members of <em>this</em>
/// collection against each other; it does not pause every other (default) collection in the assembly. It is still the
/// right tool here: the two kinds of test in this group are the ones observed to interfere with each other, so keeping
/// them out of each other's way removes that specific cause of flakiness even though some general CPU contention
/// from the rest of the assembly can still occur.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WriteLockTimingGroup
{
    public const string Name = "Serial: write-lock timing";
}
