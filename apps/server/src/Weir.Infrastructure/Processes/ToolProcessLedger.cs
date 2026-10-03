using System.ComponentModel;
using System.Diagnostics;

namespace Weir.Infrastructure.Processes;

/// <summary>
/// The processor time Weir's external tools (ffmpeg, ffprobe, mkvmerge) have used since Weir started, kept so the System
/// view can say how much of the machine they are using. <see cref="ProcessRunner"/> tracks every child it starts; a child's
/// time stays counted after it exits, so the total only ever grows.
/// </summary>
public sealed class ToolProcessLedger
{
    private readonly Lock _gate = new();
    private readonly List<TrackedProcess> _running = [];
    private TimeSpan _finished;

    /// <summary>Counts <paramref name="process"/> until the returned lease is disposed, which must happen before the process is.</summary>
    public IDisposable Track(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var tracked = new TrackedProcess(process);
        lock (_gate)
        {
            _running.Add(tracked);
        }

        return new Lease(this, tracked);
    }

    /// <summary>How many tools are running now.</summary>
    public int Running
    {
        get
        {
            lock (_gate)
            {
                return _running.Count;
            }
        }
    }

    /// <summary>The processor time of every tool, finished or running.</summary>
    public TimeSpan TotalProcessorTime
    {
        get
        {
            lock (_gate)
            {
                var total = _finished;
                foreach (var tracked in _running)
                {
                    total += tracked.ProcessorTime();
                }

                return total;
            }
        }
    }

    private void Release(TrackedProcess tracked)
    {
        lock (_gate)
        {
            if (_running.Remove(tracked))
            {
                _finished += tracked.ProcessorTime();
            }
        }
    }

    /// <summary>A tool and the last processor time read from it, which stands in once the system no longer answers for it.</summary>
    private sealed class TrackedProcess(Process process)
    {
        private TimeSpan _lastSeen;

        public TimeSpan ProcessorTime()
        {
            try
            {
                _lastSeen = process.TotalProcessorTime;
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // The tool has exited and the system has forgotten it: what was seen last is what it used.
            }

            return _lastSeen;
        }
    }

    private sealed class Lease(ToolProcessLedger owner, TrackedProcess tracked) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release(tracked);
            }
        }
    }
}
