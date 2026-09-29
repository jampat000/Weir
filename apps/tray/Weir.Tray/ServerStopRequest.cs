using System.ComponentModel;
using System.Security.AccessControl;

namespace Weir.Tray;

/// <summary>
/// Asking a running server to stop (#833). The server has no window and no console, so it listens for a named kernel
/// event of its process, <c>Local\Weir-Stop-{pid}</c>, and shuts down through its host when the event is set. The
/// name is the server's own (apps/server Weir.Host StopRequest). Only processes of the same Windows user in the same
/// session can open it. This file is also compiled into the tests' stand-in server, so it depends on nothing else.
/// </summary>
static class ServerStopRequest
{
    internal static string EventName(int processId) => FormattableString.Invariant($"Local\\Weir-Stop-{processId}");

    /// <summary>
    /// Sets the stop event of the server with this process id. When it cannot, <paramref name="failure"/> says why:
    /// the server has no such event because it is still starting, is an older build, or belongs to another user.
    /// </summary>
    internal static bool TrySend(int processId, out string failure)
    {
        try
        {
            using var stopEvent = EventWaitHandleAcl.OpenExisting(EventName(processId), EventWaitHandleRights.Modify);
            failure = string.Empty;
            return stopEvent.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException or Win32Exception)
        {
            failure = ex.Message;
            return false;
        }
    }
}
