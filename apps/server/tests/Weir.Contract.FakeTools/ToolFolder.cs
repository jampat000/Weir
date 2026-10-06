using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Weir.Contract.FakeTools;

/// <summary>
/// The folder the fake tools live in: the script a test wrote, the log of every call, and the per-name counters.
/// Tool processes run side by side, so everything that changes a file takes an exclusive lock first.
/// </summary>
internal sealed class ToolFolder(string path)
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(10);

    public string Path { get; } = path;

    public static ToolFolder Locate()
    {
        var configured = Environment.GetEnvironmentVariable(FakeToolProtocol.ToolFolderVariable);
        return new ToolFolder(string.IsNullOrEmpty(configured) ? AppContext.BaseDirectory : configured);
    }

    public JsonObject LoadScript()
    {
        try
        {
            using var stream = new FileStream(
                System.IO.Path.Combine(Path, FakeToolProtocol.ScriptFile), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(stream) as JsonObject ?? new JsonObject();
        }
        catch (Exception problem) when (problem is IOException or JsonException or UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }

    public void LogCall(string tool, IReadOnlyList<string> argv, params (string Name, JsonNode Value)[] extra)
    {
        var entry = new JsonObject
        {
            [FakeToolProtocol.ToolKey] = tool,
            [FakeToolProtocol.ArgvKey] = new JsonArray(argv.Select(argument => (JsonNode)JsonValue.Create(argument)!).ToArray()),
            [FakeToolProtocol.AtKey] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
        };
        foreach (var (name, value) in extra)
        {
            entry[name] = value;
        }

        var line = Encoding.UTF8.GetBytes(entry.ToJsonString() + "\n");
        WithLock(System.IO.Path.Combine(Path, FakeToolProtocol.CallsFile), file =>
        {
            file.Seek(0, SeekOrigin.End);
            file.Write(line);
        });
    }

    /// <summary>How many times <paramref name="key"/> has been seen, including this time.</summary>
    public int Bump(string key)
    {
        var count = 0;
        WithLock(System.IO.Path.Combine(Path, FakeToolProtocol.CountersFile), file =>
        {
            JsonObject counters;
            try
            {
                counters = file.Length == 0 ? new JsonObject() : JsonNode.Parse(file) as JsonObject ?? new JsonObject();
            }
            catch (JsonException)
            {
                counters = new JsonObject();
            }

            count = (counters[key]?.GetValue<int>() ?? 0) + 1;
            counters[key] = count;
            file.SetLength(0);
            file.Write(Encoding.UTF8.GetBytes(counters.ToJsonString()));
        });
        return count;
    }

    // The OS releases the lock if its holder dies, so a waiter never has to steal it.
    private static void WithLock(string path, Action<FileStream> action)
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            FileStream file;
            try
            {
                file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(LockRetry);
                continue;
            }

            using (file)
            {
                action(file);
            }

            return;
        }
    }
}
