using System.Diagnostics;

namespace Weir.Infrastructure.SystemReadings;

/// <summary>Weir's own process: the processor time it has used and the memory it holds.</summary>
public readonly record struct OwnProcessUse(TimeSpan ProcessorTime, long WorkingSetBytes)
{
    public static OwnProcessUse OfThisProcess()
    {
        using var process = Process.GetCurrentProcess();
        return new OwnProcessUse(process.TotalProcessorTime, process.WorkingSet64);
    }
}

/// <summary>
/// Takes one reading of everything the System view traces: the machine, Weir's own process, the tools it runs and the
/// files being processed. It owns the baselines the rates are measured against, so the sampler is its only caller.
/// </summary>
public sealed class SystemStatsCollector
{
    private readonly MachineMeter _machine;
    private readonly ProcessingThroughput _processing;
    private readonly Func<TimeSpan> _toolProcessorTime;
    private readonly Func<OwnProcessUse> _ownProcess;
    private readonly CpuShareMeter _ownShare;
    private readonly CpuShareMeter _toolShare;
    private readonly TimeProvider _time;

    public SystemStatsCollector(
        MachineMeter machine,
        ProcessingThroughput processing,
        Func<TimeSpan> toolProcessorTime,
        Func<OwnProcessUse> ownProcess,
        TimeProvider time,
        int cores)
    {
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
        _processing = processing ?? throw new ArgumentNullException(nameof(processing));
        _toolProcessorTime = toolProcessorTime ?? throw new ArgumentNullException(nameof(toolProcessorTime));
        _ownProcess = ownProcess ?? throw new ArgumentNullException(nameof(ownProcess));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        Cores = cores;
        _ownShare = new CpuShareMeter(time, cores);
        _toolShare = new CpuShareMeter(time, cores);
    }

    /// <summary>How many processor cores the machine, or the container's limit, has.</summary>
    public int Cores { get; }

    /// <param name="slots">How many files Weir will process at once, which the caller reads from its settings.</param>
    public StatsSample Collect(int slots)
    {
        var machine = _machine.Read();
        var own = _ownProcess();
        var processing = _processing.Read();
        return StatsSample.Of(new StatsNow(
            _time.GetUtcNow(),
            machine.CpuPercent,
            Cores,
            machine.MemoryUsedBytes,
            machine.MemoryTotalBytes,
            machine.DiskReadBytesPerSecond,
            machine.DiskWriteBytesPerSecond,
            machine.DiskBusyPercent,
            _ownShare.Read(own.ProcessorTime),
            own.WorkingSetBytes,
            _toolShare.Read(_toolProcessorTime()),
            processing.ReadBytesPerSecond,
            processing.WriteBytesPerSecond,
            processing.Speed,
            processing.Running,
            slots));
    }
}
