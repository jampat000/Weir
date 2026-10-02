using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Weir.Infrastructure.SystemReadings;

/// <summary>
/// The Windows readings: kernel calls for load, memory, disk counters and free space, and the registry for the system's
/// name and a pending restart.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsHostReadingSource : IHostReadingSource
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string ServicingRebootKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    private const string UpdateRebootKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    private const string SessionManagerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager";
    private const string PendingRenamesValue = "PendingFileRenameOperations";

    /// <summary>The first Windows 11 build; its registry still says "Windows 10" in the product name.</summary>
    private const int FirstWindows11Build = 22000;

    private readonly Lazy<string?> _operatingSystem = new(ReadOperatingSystemName);

    public CpuTimes? ReadCpuTimes() => Guarded(WindowsNative.ReadCpuTimes);

    public MemoryReading? ReadMemory() => Guarded(WindowsNative.ReadMemory);

    public long? ReadUptimeSeconds() => Environment.TickCount64 / 1000;

    public string? ReadOperatingSystem() => _operatingSystem.Value;

    public bool? ReadRebootPending()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return KeyExists(machine, ServicingRebootKey)
                || KeyExists(machine, UpdateRebootKey)
                || HasPendingRenames(machine);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public DiskSnapshot ReadDisks()
    {
        var volumes = new Dictionary<string, DiskCounters>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady
                    && WindowsNative.ReadVolumeCounters(drive.RootDirectory.FullName) is { } counters)
                {
                    volumes[KeyOf(drive.RootDirectory.FullName)] = counters;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DiskSnapshot.Empty;
        }

        return DiskSnapshot.FromVolumes(volumes);
    }

    public string? DriveRootOf(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrWhiteSpace(root) ? null : root.TrimEnd('\\') + '\\';
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public string? VolumeKeyOf(string driveRoot)
    {
        if (driveRoot.StartsWith(@"\\", StringComparison.Ordinal) || !driveRoot.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return new DriveInfo(driveRoot).DriveType == DriveType.Fixed ? KeyOf(driveRoot) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public DriveSpace? ReadSpace(string driveRoot) => Guarded(() => WindowsNative.ReadSpace(driveRoot));

    private static string KeyOf(string root) => root.TrimEnd('\\', '/').ToUpperInvariant();

    /// <summary>Runs a kernel call, answering null on a system where the call is not there.</summary>
    private static T? Guarded<T>(Func<T?> read)
        where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static bool KeyExists(RegistryKey machine, string path)
    {
        using var key = machine.OpenSubKey(path);
        return key is not null;
    }

    private static bool HasPendingRenames(RegistryKey machine)
    {
        using var key = machine.OpenSubKey(SessionManagerKey);
        return key?.GetValue(PendingRenamesValue) is Array { Length: > 0 } or string { Length: > 0 };
    }

    private static string? ReadOperatingSystemName()
    {
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = machine.OpenSubKey(CurrentVersionKey);
            if (key?.GetValue("ProductName") is string { Length: > 0 } product)
            {
                var build = int.TryParse(key.GetValue("CurrentBuildNumber") as string, CultureInfo.InvariantCulture, out var number) ? number : 0;
                return build >= FirstWindows11Build ? product.Replace("Windows 10", "Windows 11", StringComparison.Ordinal) : product;
            }
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Falls through to the framework's description.
        }

        return RuntimeInformation.OSDescription;
    }
}
