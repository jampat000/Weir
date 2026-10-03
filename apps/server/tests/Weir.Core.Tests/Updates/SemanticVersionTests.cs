using Weir.Core.Updates;

namespace Weir.Core.Tests.Updates;

public sealed class SemanticVersionTests
{
    private static SemanticVersion Parse(string text) =>
        SemanticVersion.TryParse(text, out var version) ? version : throw new InvalidOperationException($"'{text}' is not a version.");

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("v1.0.0")]
    [InlineData(" 3.2.16 ")]
    [InlineData("1.0.0-rc.1")]
    [InlineData("v1.0.0-beta.1")]
    [InlineData("1.0.0-rc.1+build.5")]
    [InlineData("0.0.1-dev")]
    public void A_semver_version_parses(string text) => Assert.True(SemanticVersion.TryParse(text, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-rc..1")]
    [InlineData("1.0.0-rc.01")]
    [InlineData("1.0.0-rc_1")]
    [InlineData("latest")]
    public void Text_that_is_not_a_semver_version_does_not_parse(string? text) => Assert.False(SemanticVersion.TryParse(text, out _));

    [Fact]
    public void Only_a_version_with_a_pre_release_part_is_a_pre_release()
    {
        Assert.True(Parse("1.0.0-rc.1").IsPreRelease);
        Assert.False(Parse("1.0.0").IsPreRelease);
        Assert.False(Parse("1.0.0+build").IsPreRelease);
    }

    [Theory]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.2")]
    [InlineData("1.0.0-rc.9", "1.0.0-rc.10")]
    [InlineData("1.0.0-rc.9", "1.0.0")]
    [InlineData("1.0.0-beta.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.1-rc.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-beta.1")]
    [InlineData("1.0.0-beta.1", "1.0.0-rc.1")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("1.0.0-rc", "1.0.0-rc.1")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("2.0.0", "10.0.0")]
    [InlineData("3.2.16", "4.0.0")]
    public void The_first_version_is_older_than_the_second(string older, string newer)
    {
        Assert.True(Parse(older) < Parse(newer));
        Assert.True(Parse(newer) > Parse(older));
        Assert.NotEqual(Parse(older), Parse(newer));
    }

    [Fact]
    public void Build_metadata_and_a_leading_v_do_not_change_the_order()
    {
        Assert.Equal(Parse("1.0.0-rc.1"), Parse("v1.0.0-rc.1+abc123"));
        Assert.False(Parse("1.0.0-rc.1") < Parse("1.0.0-rc.1+abc123"));
    }

    [Fact]
    public void A_version_with_digits_beyond_a_machine_integer_still_compares()
    {
        Assert.True(Parse("1.0.99999999999999999999") < Parse("1.0.100000000000000000000"));
    }
}
