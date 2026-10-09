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

// A server that cannot start: when fail-start.txt sits beside it, it counts the start in starts.txt, writes that file's text to
// startup-error.txt in WEIR_HOME as the real server does when it refuses to start, and exits at once with code 1.
var failStart = Path.Combine(AppContext.BaseDirectory, "fail-start.txt");
if (File.Exists(failStart))
{
    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "starts.txt"), "start\n");
    File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("WEIR_HOME")!, "startup-error.txt"), File.ReadAllText(failStart));
    return LifetimeEndedExitCode;
}

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
