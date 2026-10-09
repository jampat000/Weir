using System.Collections.Concurrent;
using System.Diagnostics;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Tests;

/// <summary>
/// Remembers the processes one test started through <see cref="ProcessRunner"/>, so it can check that those are gone
/// without counting by name, which other tests and other programs running the same tool would throw off. Use it as the
/// runner a tool gets, or pass <see cref="Remember"/> as <see cref="ProcessRequest.OnStarted"/>.
/// </summary>
internal sealed class StartedProcesses : IProcessRunner, IDisposable
{
    private readonly ProcessRunner _runner = new();
    private readonly ConcurrentQueue<Process?> _started = new();

    public int Count => _started.Count;

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default) =>
        _runner.RunAsync(request with { OnStarted = Remember }, cancellationToken);

    /// <summary>Holds a handle to the process now, so its end can be seen later even if the system reuses its id.</summary>
    public void Remember(int processId)
    {
        try
        {
            _started.Enqueue(Process.GetProcessById(processId));
        }
        catch (ArgumentException)
        {
            // It ended before it could be opened: nothing is left to wait for.
            _started.Enqueue(null);
        }
    }

    /// <summary>Fails unless every remembered process has ended, or ends within the ceiling.</summary>
    public Task AllHaveEndedAsync() => Eventually.ThatAsync(() => _started.All(process => process is null || process.HasExited));

    public void Dispose()
    {
        foreach (var process in _started)
        {
            process?.Dispose();
        }
    }
}
