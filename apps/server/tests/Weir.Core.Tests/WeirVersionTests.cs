using Weir.Core.Updates;

namespace Weir.Core.Tests;

public sealed class WeirVersionTests
{
    [Theory]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.1")]
    [InlineData("  1.0.0-rc.1  ", "1.0.0-rc.1")]
    [InlineData("3.2.16", "3.2.16")]
    [InlineData("1.0.0-rc.4+abc1234", "1.0.0-rc.4+abc1234")]
    public void The_override_is_reported_as_given(string versionOverride, string expected) =>
        Assert.Equal(expected, WeirVersion.Resolve(versionOverride));

    [Fact]
    public void Without_an_override_the_build_version_is_reported() =>
        Assert.Equal(WeirVersion.BuildVersion, WeirVersion.Resolve(" "));

    [Fact]
    public void A_stamped_build_compares_as_its_release()
    {
        Assert.True(SemanticVersion.TryParse("1.0.0-rc.4+abc1234", out var stamped));
        Assert.True(SemanticVersion.TryParse("1.0.0-rc.4", out var release));
        Assert.True(SemanticVersion.TryParse("1.0.0-rc.3", out var previous));
        Assert.Equal(0, stamped.CompareTo(release));
        Assert.True(stamped > previous);
    }
}
