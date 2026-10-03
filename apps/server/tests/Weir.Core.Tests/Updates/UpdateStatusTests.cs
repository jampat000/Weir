using Weir.Core.Json;
using Weir.Core.Updates;

namespace Weir.Core.Tests.Updates;

public sealed class UpdateStatusTests
{
    private static GitHubReleaseRecord Release(string version) =>
        new($"v{version}", version, null, null, null, false, version.Contains('-', StringComparison.Ordinal), []);

    private static string StatusOf(string running, string released) =>
        ((WireString)UpdateStatus.FromRelease(running, "windows", Release(released))["status"]).Value;

    [Theory]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.2")]
    [InlineData("1.0.0-rc.9", "1.0.0-rc.10")]
    [InlineData("1.0.0-rc.9", "1.0.0")]
    [InlineData("3.2.16", "4.0.0")]
    public void A_newer_release_is_an_update(string running, string released) =>
        Assert.Equal("update_available", StatusOf(running, released));

    [Theory]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.2")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0", "1.0.0-rc.9")]
    [InlineData("1.0.0", "1.0.0")]
    public void A_release_that_is_not_newer_is_not_an_update(string running, string released) =>
        Assert.Equal("up_to_date", StatusOf(running, released));

    [Fact]
    public void A_pre_release_docker_install_is_pointed_at_the_pre_release_tag()
    {
        var status = UpdateStatus.FromRelease("1.0.0-rc.1", "docker", Release("1.0.0-rc.2"));

        Assert.Equal("1.0.0-rc.2", ((WireString)status["docker_tag"]).Value);
    }

    [Fact]
    public void The_release_list_payload_reads_every_release_including_pre_releases_and_drafts()
    {
        var payload = WireJsonParser.Parse(
            """[{"tag_name":"v1.0.0-rc.2","prerelease":true,"draft":false},{"tag_name":"v1.0.0-rc.1","prerelease":true,"draft":true}]""");

        var releases = ReleaseCatalog.CoerceReleaseListPayload(payload);

        Assert.Equal(["1.0.0-rc.2", "1.0.0-rc.1"], releases.Select(release => release.Version));
        Assert.Equal([true, true], releases.Select(release => release.Prerelease));
        Assert.Equal([false, true], releases.Select(release => release.Draft));
    }

    [Fact]
    public void A_release_list_payload_that_is_not_a_list_is_refused()
    {
        var payload = WireJsonParser.Parse("""{"message":"Not Found"}""");

        Assert.Throws<WireValueException>(() => ReleaseCatalog.CoerceReleaseListPayload(payload));
    }
}
