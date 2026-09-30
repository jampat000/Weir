using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// How an update is handed to Velopack's updater. Velopack shows its own install window unless it is told not to, so
/// each way of installing an update has to ask for it to stay hidden (#869).
/// </summary>
public sealed class UpdateHandOverTests
{
    [Fact]
    public void Quitting_to_update_asks_for_no_install_window_and_does_not_start_weir_again()
    {
        var handOver = UpdateHandOver.ThenStayStopped;

        Assert.True(handOver.Silent);
        Assert.False(handOver.Restart);
        Assert.Empty(handOver.RestartArguments);
    }

    [Fact]
    public void Restarting_to_update_asks_for_no_install_window_and_starts_weir_again_without_the_browser()
    {
        var handOver = UpdateHandOver.ThenRestart;

        Assert.True(handOver.Silent);
        Assert.True(handOver.Restart);
        Assert.Equal([Program.NoBrowserArgument], handOver.RestartArguments);
    }
}
