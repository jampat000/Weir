using System.Runtime.InteropServices;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// <see cref="PhysicalSwapFileSystem"/>'s POSIX-only interop: the <c>libc</c> calls its cross-platform operations
/// branch into on Linux and other non-Windows platforms, kept apart from those operations' own logic.
/// </summary>
public sealed partial class PhysicalSwapFileSystem
{
    private const int Ebusy = 16;
    private const int Etxtbsy = 26;
    private const int Exdev = 18;

    private readonly record struct UnixFileStat(uint Links, uint Uid, uint Gid);

    private static UnixFileStat? UnixStat(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        const int atFdCwd = -100;
        const uint statxNlink = 0x4;
        const uint statxUid = 0x8;
        const uint statxGid = 0x10;
        // struct statx is the same on every Linux architecture: stx_mask (u32) at 0, stx_nlink at 16, stx_uid at 20, stx_gid at 24.
        var buffer = new byte[256];
        try
        {
            if (Statx(atFdCwd, path, 0, statxNlink | statxUid | statxGid, ref buffer[0]) != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                throw errno == 2 ? new FileNotFoundException($"No such file: '{path}'", path) : new IOException($"statx failed with errno {errno} for '{path}'");
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        var mask = BitConverter.ToUInt32(buffer, 0);
        if ((mask & statxNlink) == 0)
        {
            return null;
        }

        return new UnixFileStat(BitConverter.ToUInt32(buffer, 16), BitConverter.ToUInt32(buffer, 20), BitConverter.ToUInt32(buffer, 24));
    }

    private static bool UnixIsRoot()
    {
        try
        {
            return Geteuid() == 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("libc", EntryPoint = "statx", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Statx(int directoryFd, string path, int flags, uint mask, ref byte buffer);

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint Geteuid();

    [LibraryImport("libc", EntryPoint = "chown", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UnixChown(string path, uint owner, uint group);
}
