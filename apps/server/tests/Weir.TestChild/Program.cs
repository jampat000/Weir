using System.Diagnostics;

// A stand-in for a slow external tool, for tests of the process runner (#806). It is a program of our own, not a system
// tool run through a shell: a console program that inherits its shell's console and outlives a kill of that shell can be
// left without a console, and where Windows Terminal is the default terminal Windows then opens an error window for it.
// The child started below gets a console of its own, so killing its parent at any moment cannot strand it.
const string Hold = "hold";
const string AnnounceAndHold = "announce-and-hold";
const string HoldThroughChild = "hold-through-child";
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
            await child!.WaitForExitAsync();
        }

        Console.Out.WriteLine(FinishedLine);
        break;

    default:
        Console.Error.WriteLine($"Unknown mode. Use {Hold}, {AnnounceAndHold} or {HoldThroughChild}.");
        return 2;
}

return 0;
