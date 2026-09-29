// A stand-in for WeirServer.exe in the tray's process tests. Like the server, it creates its stop event
// (Weir.Tray.ServerStopRequest), says "ready" on its standard output, and exits with code 0 when the event is set.
// "ignore-stop-requests" leaves the event unhonoured and "no-stop-event" never creates it; either way it stays alive
// until it is killed. It is a program of our own on the Windows subsystem, not a system console program such as
// ping: a console program can be handed a console window when its parent is killed, and where Windows Terminal is the
// default terminal that opens a window on the desktop (#806, #821).
using Weir.Tray;

const string IgnoreStopRequests = "ignore-stop-requests";
const string NoStopEvent = "no-stop-event";
const string ReadyLine = "ready";
const int StoppedExitCode = 0;
const int LifetimeEndedExitCode = 1;

// Bounds how long a process orphaned by a killed test run can linger.
var lifetime = TimeSpan.FromMinutes(2);

using var stopEvent = args.Contains(NoStopEvent)
    ? null
    : new EventWaitHandle(initialState: false, EventResetMode.ManualReset, ServerStopRequest.EventName(Environment.ProcessId));
Console.Out.WriteLine(ReadyLine);
Console.Out.Flush();

if (stopEvent is null || args.Contains(IgnoreStopRequests))
{
    await Task.Delay(lifetime);
    return LifetimeEndedExitCode;
}

return stopEvent.WaitOne(lifetime) ? StoppedExitCode : LifetimeEndedExitCode;
