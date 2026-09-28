using Weir.Tray.Firewall;
using Xunit;

namespace Weir.Tray.Tests.Firewall;

/// <summary>The first-run "Allow Weir on your network?" prompt asks at most once, whatever the answer was.</summary>
public sealed class FirewallPromptFileTests : IDisposable
{
    private readonly TempDirectory _temp = TempDirectory.Create();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_fresh_install_has_not_been_asked_yet()
    {
        Assert.False(FirewallPromptFile.AlreadyAsked(_temp.Path));
    }

    [Fact]
    public void Marking_a_decline_as_asked_still_counts_as_asked()
    {
        FirewallPromptFile.MarkAsked(_temp.Path, FirewallElevation.Outcome.Declined);

        Assert.True(FirewallPromptFile.AlreadyAsked(_temp.Path));
    }

    [Fact]
    public void Marking_success_as_asked_counts_as_asked()
    {
        FirewallPromptFile.MarkAsked(_temp.Path, FirewallElevation.Outcome.Configured);

        Assert.True(FirewallPromptFile.AlreadyAsked(_temp.Path));
    }
}
