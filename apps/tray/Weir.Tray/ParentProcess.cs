using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Weir.Tray;

/// <summary>
/// The process that started this one. An installer hook is started by Setup, and the hook needs Setup's id to wait for
/// its exit; .NET has no way to ask for a parent.
/// </summary>
static class ParentProcess
{
    private const int ProcessBasicInformationClass = 0;

    // Classic DllImport, like InheritedStdioHandles: the source-generated form needs <AllowUnsafeBlocks>.
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint processHandle,
        int informationClass,
        ref ProcessBasicInformation information,
        int informationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    /// <summary>The id of the process that started this one, or null when Windows does not say.</summary>
    internal static int? Id()
    {
        using var self = Process.GetCurrentProcess();
        var information = new ProcessBasicInformation();
        var status = NtQueryInformationProcess(
            self.Handle,
            ProcessBasicInformationClass,
            ref information,
            Marshal.SizeOf<ProcessBasicInformation>(),
            out _);
        return status == 0 ? (int)information.InheritedFromUniqueProcessId : null;
    }
}
