using Xunit;

namespace Weir.Tray.Tests;

/// <summary>A second launch tells the Weir that is running, and a silent one stays silent.</summary>
public sealed class SecondLaunchTests : IDisposable
{
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly string _name = $@"Local\WeirTrayTests-{Guid.NewGuid():n}";
    private readonly SemaphoreSlim _heard = new(0);

    public void Dispose()
    {
        _heard.Dispose();
        _home.Dispose();
    }

    [Fact]
    public void A_second_launch_reaches_the_running_tray_each_time()
    {
        using var listening = new SecondLaunchSignal(() => _heard.Release(), _name);

        SecondLaunchSignal.Raise(_name);
        Assert.True(_heard.Wait(Ceiling));
        SecondLaunchSignal.Raise(_name);
        Assert.True(_heard.Wait(Ceiling));
    }

    [Fact]
    public void With_no_tray_listening_a_second_launch_has_nobody_to_tell_and_does_no_harm()
    {
        SecondLaunchSignal.Raise(_name);
    }

    [Fact]
    public void The_running_tray_is_told_when_Weir_is_started_again_by_hand_or_at_sign_in()
    {
        foreach (var args in new[] { Array.Empty<string>(), ["--no-browser"], ["--port", "9400"] })
        {
            var told = false;

            Program.HandOverToRunningTray(args, () => told = true);

            Assert.True(told, string.Join(' ', args));
        }
    }

    [Fact]
    public void A_silent_second_launch_tells_nobody()
    {
        var told = false;

        Program.HandOverToRunningTray(["--silent"], () => told = true);
        Program.HandOverToRunningTray(["--no-browser", "--silent"], () => told = true);

        Assert.False(told);
    }

    [Fact]
    public void A_second_launch_opens_no_browser()
    {
        Program.HandOverToRunningTray([], () => { });

        Assert.DoesNotContain("Opening Weir in browser", File.ReadAllText(Path.Combine(_home.Path, "tray-host.log")), StringComparison.Ordinal);
    }
}
