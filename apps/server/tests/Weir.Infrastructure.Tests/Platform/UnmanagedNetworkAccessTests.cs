using Weir.Core.Configuration;
using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Tests.Platform;

public sealed class UnmanagedNetworkAccessTests
{
    private static readonly ServerListenOptions Listen = new("0.0.0.0", 9347);

    [Fact]
    public void Docker_says_its_port_mapping_decides()
    {
        var access = new UnmanagedNetworkAccess(ServerRunMode.Docker, Listen);

        Assert.StartsWith("Set by Docker's port mapping", access.NotChangeableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_install_points_to_the_bind_option()
    {
        var access = new UnmanagedNetworkAccess(ServerRunMode.App, Listen);

        Assert.Contains("--host", access.NotChangeableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void It_reports_not_applicable_with_the_port()
    {
        var status = new UnmanagedNetworkAccess(ServerRunMode.Docker, Listen).Read();

        Assert.Equal(NetworkAccessState.NotApplicable, status.State);
        Assert.Null(status.Scope);
        Assert.Equal(9347, status.Port);
    }

    [Fact]
    public void Choosing_is_refused()
    {
        var access = new UnmanagedNetworkAccess(ServerRunMode.Docker, Listen);

        Assert.Throws<InvalidOperationException>(() => access.Choose(NetworkScope.Network));
    }
}
