using System.Runtime.InteropServices;

namespace Weir.Tray;

/// <summary>
/// Puts a window the person must answer in front of whatever else is open. Windows only lets the process that owns the
/// foreground hand it on, and a tray started by hand or at sign-in does not own it, so a plain <see cref="Form.Activate"/>
/// leaves the window behind Server Manager or whatever else is up. The window is made top-most, the foreground is taken the way
/// Windows allows (by joining the input of the thread that holds it), and when it still cannot be taken the taskbar button
/// flashes until the person comes to it.
/// </summary>
static class ForegroundWindow
{
    private static readonly IntPtr TopMost = new(-1);
    private const uint NoMove = 0x0002;
    private const uint NoSize = 0x0001;
    private const uint ShowWindow = 0x0040;
    private const uint FlashAll = 0x0003;
    private const uint FlashUntilForeground = 0x000C;

    /// <summary>Whether the window is top-most right now, as Windows sees it.</summary>
    internal static bool IsTopMost(Form form) => (GetWindowLong(form.Handle, ExStyle) & TopMostStyle) != 0;

    private const int ExStyle = -20;
    private const int TopMostStyle = 0x00000008;

    /// <summary>Makes <paramref name="form"/> top-most and the foreground window, or flashes its taskbar button when Windows will not allow that.</summary>
    internal static void Bring(Form form)
    {
        var handle = form.Handle;
        SetWindowPos(handle, TopMost, 0, 0, 0, 0, NoMove | NoSize | ShowWindow);
        form.Activate();
        if (!SetForegroundWindow(handle))
        {
            TakeForegroundThroughTheHolder(handle);
        }
        if (GetForegroundWindow() != handle)
        {
            Flash(handle);
        }
    }

    private static void TakeForegroundThroughTheHolder(IntPtr handle)
    {
        var holderThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var thisThread = GetCurrentThreadId();
        if (holderThread == 0 || holderThread == thisThread || !AttachThreadInput(thisThread, holderThread, true))
        {
            return;
        }
        try
        {
            SetForegroundWindow(handle);
        }
        finally
        {
            AttachThreadInput(thisThread, holderThread, false);
        }
    }

    private static void Flash(IntPtr handle)
    {
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle, Flags = FlashAll | FlashUntilForeground, Count = uint.MaxValue };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool attached);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
