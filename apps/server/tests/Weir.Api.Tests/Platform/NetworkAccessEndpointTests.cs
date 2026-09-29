using Weir.Api.Endpoints;
using Weir.Core.Json;
using Weir.Infrastructure.Runtime;

namespace Weir.Api.Tests.Platform;

public sealed class NetworkAccessEndpointTests
{
    private static string Field(NetworkAccessState state, string name) =>
        Assert.IsType<WireString>(NetworkAccessStatusWire.From(state)[name]).Value;

    [Theory]
    [InlineData(NetworkAccessState.ThisPcOnly, "this_pc_only", "Only this PC can reach Weir.")]
    [InlineData(NetworkAccessState.Allowed, "allowed", "Other devices on your network can reach Weir.")]
    [InlineData(NetworkAccessState.Blocked, "blocked", "Windows Firewall is blocking other devices.")]
    public void Each_state_says_who_can_reach_weir_in_plain_words(NetworkAccessState state, string wireState, string summaryStart)
    {
        Assert.Equal(wireState, Field(state, "state"));
        Assert.StartsWith(summaryStart, Field(state, "summary"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NetworkAccessState.ThisPcOnly, "Allow other devices on your network")]
    [InlineData(NetworkAccessState.Allowed, "Only allow this PC")]
    [InlineData(NetworkAccessState.Blocked, "Allow other devices on your network")]
    public void Each_state_names_the_tray_item_that_changes_it(NetworkAccessState state, string trayItem)
    {
        Assert.Contains(trayItem, Field(state, "summary"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_platform_that_does_not_manage_its_own_exposure_reports_not_applicable_with_no_summary()
    {
        Assert.Equal("not_applicable", Field(NetworkAccessState.NotApplicable, "state"));
        Assert.Equal("", Field(NetworkAccessState.NotApplicable, "summary"));
    }
}
