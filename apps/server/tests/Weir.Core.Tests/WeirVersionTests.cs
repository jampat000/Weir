namespace Weir.Core.Tests;

public sealed class WeirVersionTests
{
    [Theory]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1")]
    [InlineData("  1.0.0-rc.1  ", "1.0.0-rc.1")]
    [InlineData("3.2.16", "3.2.16")]
    public void The_override_is_reported_with_its_pre_release_part(string versionOverride, string expected) =>
        Assert.Equal(expected, WeirVersion.Resolve(versionOverride));

    [Theory]
    [InlineData("1.0.0-rc.1+abc123", "1.0.0-rc.1")]
    [InlineData("1.0.0+abc123", "1.0.0")]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1")]
    public void Build_metadata_is_dropped_and_the_pre_release_part_kept(string informational, string expected) =>
        Assert.Equal(expected, WeirVersion.WithoutBuildMetadata(informational));

    [Fact]
    public void Without_an_override_the_build_version_is_reported() =>
        Assert.Equal(WeirVersion.BuildVersion, WeirVersion.Resolve(" "));
}
