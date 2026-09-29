using Weir.Core.Configuration;

namespace Weir.Core.Tests.Configuration;

/// <summary>Weir is named after the machine it runs on, and knows when that name is a container's generated one.</summary>
public sealed class MachineIdentityTests
{
    [Theory]
    [InlineData("RIG", "Weir on RIG")]
    [InlineData("nas-01", "Weir on nas-01")]
    [InlineData("media.example.lan", "Weir on media.example.lan")]
    public void The_app_introduces_itself_by_the_host_name(string hostName, string expected)
    {
        Assert.Equal(expected, MachineIdentity.From(hostName).AppName);
    }

    [Theory]
    [InlineData("  RIG  ", "RIG")]
    [InlineData("", "this computer")]
    [InlineData(null, "this computer")]
    public void A_host_name_is_trimmed_and_a_blank_one_reads_as_this_computer(string? hostName, string expected)
    {
        Assert.Equal(expected, MachineIdentity.From(hostName).Name);
    }

    [Theory]
    [InlineData("3f9a1c0d7b2e")]
    [InlineData("0123456789ab")]
    public void A_twelve_character_hex_host_name_looks_generated(string hostName)
    {
        Assert.True(MachineIdentity.From(hostName).LooksGenerated);
    }

    [Theory]
    [InlineData("RIG")]
    [InlineData("3f9a1c0d7b2")]
    [InlineData("3f9a1c0d7b2ef")]
    [InlineData("3F9A1C0D7B2E")]
    [InlineData("nas-server1")]
    [InlineData("zzzzzzzzzzzz")]
    public void A_host_name_someone_chose_does_not_look_generated(string hostName)
    {
        Assert.False(MachineIdentity.From(hostName).LooksGenerated);
    }

    [Fact]
    public void This_machines_identity_is_the_operating_systems_host_name()
    {
        Assert.Equal(Environment.MachineName, MachineIdentity.Current().Name);
    }
}
