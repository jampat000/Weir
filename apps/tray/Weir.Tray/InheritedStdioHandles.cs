using System.Runtime.InteropServices;

namespace Weir.Tray;

/// <summary>
/// Closes any stdio handles Weir.exe inherited from whatever started it, before it settles in as a
/// long-running background app.
///
/// A caller that redirects a child process's stdout and stderr through pipes and waits for those
/// pipes to reach end-of-file — the ordinary way to capture an installer's output — blocks until
/// every process holding the write end closes it, not just the process it launched. Windows hands
/// the same inheritable handles down to whatever that process spawns in turn. Weir.exe runs until
/// Quit, and starts its own child, WeirServer.exe; left alone, both would hold an inherited pipe
/// open indefinitely, and a caller waiting for end-of-file would wait forever too, even though the
/// process it actually launched (Setup.exe, or Weir.exe itself) already exited (#779). Closing the
/// handles here, before anything else runs, stops them reaching WeirServer.exe as well.
/// </summary>
static class InheritedStdioHandles
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    // Classic DllImport, not LibraryImport: the source-generated form needs <AllowUnsafeBlocks> for
    // its bool-returning marshalling, which is more than this one file needs to ask of the project.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int nStdHandle, nint hHandle);

    private static readonly (string Name, int Id)[] Handles =
    [
        ("stdin", StdInputHandle),
        ("stdout", StdOutputHandle),
        ("stderr", StdErrorHandle),
    ];

    /// <summary>
    /// Best-effort: closing a handle Weir never opened itself must never stop Weir from starting, so
    /// every failure here is logged and skipped rather than thrown.
    /// </summary>
    internal static void CloseInherited(Action<string> log)
    {
        foreach (var (name, id) in Handles)
        {
            var handle = GetStdHandle(id);

            // No handle was ever inherited (the common case: a person double-clicking Weir, or a
            // launcher that did not redirect it), or it is already invalid.
            if (handle == nint.Zero || handle == new nint(-1))
            {
                continue;
            }

            if (!CloseHandle(handle))
            {
                log($"Could not close the inherited {name} handle (Win32 error {Marshal.GetLastWin32Error()}).");
                continue;
            }

            SetStdHandle(id, nint.Zero);
        }
    }
}
