using System.Diagnostics;
using System.Globalization;

// A stand-in for a slow external tool, for tests of the process runner (#806). It is a program of our own, not a system
// tool run through a shell: a console program that inherits its shell's console and outlives a kill of that shell can be
// left without a console, and where Windows Terminal is the default terminal Windows then opens an error window for it.
// The child started below gets a console of its own, so killing its parent at any moment cannot strand it.
const string Hold = "hold";
const string AnnounceAndHold = "announce-and-hold";
const string HoldThroughChild = "hold-through-child";
const string CloseStdoutThenLinger = "close-stdout-then-linger";
const string Chatter = "chatter";
const string ChatterThenStall = "chatter-then-stall";
const string TickLine = "tick";
const string AnnouncementLine = "ready";
const string FinishedLine = "done";

// Bounds how long a process orphaned by a killed test run can linger.
var lifetime = TimeSpan.FromMinutes(1);

switch (args.FirstOrDefault())
{
    case Hold:
        await Task.Delay(lifetime);
        break;

    case AnnounceAndHold:
        Console.Out.WriteLine(AnnouncementLine);
        await Task.Delay(lifetime);
        break;

    case HoldThroughChild:
        // The child inherits this process's stdout and stderr, so it keeps those pipes open if only this process is killed.
        using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Hold) { UseShellExecute = false, CreateNoWindow = true }))
        {
            // A second argument names a file to receive the child's process id, so a test can check the child is gone. It
            // appears whole or not at all, because the test may read it at the moment this process is killed.
            if (args.Length > 1)
            {
                var partial = args[1] + ".partial";
                await File.WriteAllTextAsync(partial, child!.Id.ToString(CultureInfo.InvariantCulture));
                File.Move(partial, args[1]);
            }

            await child!.WaitForExitAsync();
        }

        Console.Out.WriteLine(FinishedLine);
        break;

    case CloseStdoutThenLinger:
        // Like an ffmpeg tearing down after its progress stream ends: stdout closes, the process takes its time to exit.
        // The last argument is the linger in seconds, because a caller that adds options of its own puts them before it.
        Console.Out.WriteLine(AnnouncementLine);
        Console.Out.Flush();
        StandardOutput.Close();
        await Task.Delay(TimeSpan.FromSeconds(double.Parse(args[^1], CultureInfo.InvariantCulture)));
        break;

    case Chatter:
    case ChatterThenStall:
        // Like an ffmpeg working through a slow read: a line of progress every <period> milliseconds, then either the end or
        // silence. The period is the second argument and the number of lines the last, because a caller that adds options of
        // its own puts them between.
        for (var tick = 0; tick < int.Parse(args[^1], CultureInfo.InvariantCulture); tick++)
        {
            Console.Out.WriteLine(TickLine);
            Console.Out.Flush();
            await Task.Delay(TimeSpan.FromMilliseconds(double.Parse(args[1], CultureInfo.InvariantCulture)));
        }

        if (args[0] == ChatterThenStall)
        {
            await Task.Delay(lifetime);
        }

        break;

    default:
        Console.Error.WriteLine($"Unknown mode. Use {Hold}, {AnnounceAndHold}, {HoldThroughChild}, {CloseStdoutThenLinger}, {Chatter} or {ChatterThenStall}.");
        return 2;
}

return 0;
