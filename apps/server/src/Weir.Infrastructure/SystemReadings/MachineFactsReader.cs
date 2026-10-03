namespace Weir.Infrastructure.SystemReadings;

/// <summary>
/// Reads what changes slowly about the machine. The system's name is read once; whether it wants a restart at most once a
/// <see cref="RebootCheckInterval"/>, since that looks in the registry or the file system; uptime on every call, because it
/// is the cheapest of them and the screen counts it up.
/// </summary>
public sealed class MachineFactsReader(IHostReadingSource source, TimeProvider time)
{
    public static readonly TimeSpan RebootCheckInterval = TimeSpan.FromSeconds(60);

    private string? _operatingSystem;
    private bool _operatingSystemRead;
    private bool? _rebootPending;
    private DateTimeOffset _rebootCheckedAt;

    public MachineFacts Read()
    {
        if (!_operatingSystemRead)
        {
            _operatingSystem = source.ReadOperatingSystem();
            _operatingSystemRead = true;
        }

        var now = time.GetUtcNow();
        if (_rebootCheckedAt == default || now - _rebootCheckedAt >= RebootCheckInterval)
        {
            _rebootPending = source.ReadRebootPending();
            _rebootCheckedAt = now;
        }

        return new MachineFacts(_operatingSystem, source.ReadUptimeSeconds(), _rebootPending);
    }
}
