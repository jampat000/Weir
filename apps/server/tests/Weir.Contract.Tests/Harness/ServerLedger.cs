using System.Diagnostics;
using System.Text.Json;

namespace Weir.Contract.Tests.Harness;

/// <summary>
/// A record of every server this test run started, so a run that was killed before its teardown cannot leave
/// servers behind. One file per server, written by the run that owns it; a later run stops only servers whose
/// owner has gone, so runs side by side never touch each other's servers.
/// </summary>
public sealed class ServerLedger
{
    private static readonly Lazy<ServerLedger> SharedLedger = new(() => new ServerLedger(DefaultFolder()));

    private readonly string _folder;

    public ServerLedger(string folder) => _folder = folder;

    public static ServerLedger Shared => SharedLedger.Value;

    private sealed record Entry(int ServerProcessId, long ServerStartTicks, int OwnerProcessId, long OwnerStartTicks);

    public void Record(Process server)
    {
        Directory.CreateDirectory(_folder);
        using var owner = Process.GetCurrentProcess();
        var entry = new Entry(server.Id, StartTicks(server), owner.Id, StartTicks(owner));
        File.WriteAllText(EntryPath(server.Id), JsonSerializer.Serialize(entry));
    }

    public void Forget(int serverProcessId) => File.Delete(EntryPath(serverProcessId));

    /// <summary>Stops every recorded server whose test run is gone, and returns how many it stopped.</summary>
    public int StopOrphans()
    {
        if (!Directory.Exists(_folder))
        {
            return 0;
        }

        var stopped = 0;
        foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
        {
            var entry = Read(file);
            if (entry is null || IsRunning(entry.OwnerProcessId, entry.OwnerStartTicks))
            {
                continue;
            }

            stopped += StopIfStillTheSameProcess(entry) ? 1 : 0;
            File.Delete(file);
        }

        return stopped;
    }

    private static bool StopIfStillTheSameProcess(Entry entry)
    {
        // The start time guards against a process id the operating system has since given to something else.
        try
        {
            using var process = Process.GetProcessById(entry.ServerProcessId);
            if (StartTicks(process) != entry.ServerStartTicks)
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsRunning(int processId, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && StartTicks(process) == startTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static Entry? Read(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<Entry>(File.ReadAllText(file));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static long StartTicks(Process process) => process.StartTime.ToUniversalTime().Ticks;

    private string EntryPath(int serverProcessId) => Path.Combine(_folder, $"{serverProcessId}.json");

    private static string DefaultFolder() => Path.Combine(Path.GetTempPath(), "weir-contract-servers");
}
