using Xunit;

namespace Weir.Tray.Tests;

public sealed class UpdateChannelTests
{
    [Theory]
    [InlineData("1.0.0-rc.1")]
    [InlineData("1.0.0-beta.2+abc123")]
    [InlineData("0.0.1-dev")]
    public void A_pre_release_install_is_offered_pre_releases(string runningVersion) =>
        Assert.True(UpdateChannel.IncludesPreReleases(runningVersion));

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("3.2.16")]
    [InlineData("1.0.0+abc-123")]
    [InlineData("")]
    [InlineData(null)]
    public void A_stable_install_is_offered_only_stable_releases(string? runningVersion) =>
        Assert.False(UpdateChannel.IncludesPreReleases(runningVersion));

    [Fact]
    public void A_pre_release_install_reads_pre_releases_from_its_github_source() =>
        Assert.True(UpdateChannel.SourceFor(UpdateService.GitHubRepo, "1.0.0-rc.1").Prerelease);

    [Fact]
    public void A_stable_install_reads_only_stable_releases_from_its_github_source() =>
        Assert.False(UpdateChannel.SourceFor(UpdateService.GitHubRepo, "1.0.0").Prerelease);
}
