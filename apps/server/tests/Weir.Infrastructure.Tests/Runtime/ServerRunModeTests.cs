using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>How Weir tells a service, an app and a container apart.</summary>
public sealed class ServerRunModeTests
{
    [Theory]
    [InlineData("docker", true, "docker")]
    [InlineData("docker", false, "docker")]
    [InlineData("windows", true, "service")]
    [InlineData("source", true, "service")]
    [InlineData("windows", false, "app")]
    [InlineData("source", false, "app")]
    public void A_container_is_docker_whatever_started_it_and_otherwise_a_service_manager_makes_a_service(
        string installType, bool startedByServiceManager, string expected)
    {
        Assert.Equal(expected, ServerRunMode.Detect(installType, startedByServiceManager).Name);
    }
}
