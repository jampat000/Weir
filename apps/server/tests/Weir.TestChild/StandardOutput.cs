using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

/// <summary>Closes this process's own stdout, so a reader sees the end of the stream while the process keeps running.</summary>
internal static partial class StandardOutput
{
    private const int WindowsStdOutputHandle = -11;
    private const int UnixStdOutputDescriptor = 1;

    public static void Close()
    {
        var handle = OperatingSystem.IsWindows() ? GetStdHandle(WindowsStdOutputHandle) : UnixStdOutputDescriptor;
        new SafeFileHandle(handle, ownsHandle: true).Dispose();
    }

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetStdHandle(int standardHandle);
}
