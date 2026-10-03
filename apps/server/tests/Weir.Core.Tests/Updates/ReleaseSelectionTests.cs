using Weir.Core.Updates;

namespace Weir.Core.Tests.Updates;

public sealed class ReleaseSelectionTests
{
    private static GitHubReleaseRecord Release(string version, bool preRelease = false, bool draft = false) =>
        new($"v{version}", version, null, null, null, draft, preRelease, []);

    private static string? Newest(string runningVersion, params GitHubReleaseRecord[] releases) =>
        ReleaseSelection.NewestFor(releases, runningVersion)?.Version;

    [Fact]
    public void A_release_candidate_is_offered_the_newest_release_candidate_by_version()
    {
        var releases = new[]
        {
            Release("1.0.0-rc.9", preRelease: true),
            Release("1.0.0-rc.10", preRelease: true),
            Release("1.0.0-rc.2", preRelease: true),
        };

        Assert.Equal("1.0.0-rc.10", Newest("1.0.0-rc.1", releases));
    }

    [Fact]
    public void The_stable_release_is_newer_than_the_release_candidates_before_it()
    {
        Assert.Equal("1.0.0", Newest("1.0.0-rc.1", Release("1.0.0-rc.9", preRelease: true), Release("1.0.0")));
    }

    [Fact]
    public void A_stable_install_is_offered_only_stable_releases()
    {
        var offered = Newest("1.0.0", Release("1.0.1-rc.1", preRelease: true), Release("1.0.0"), Release("0.9.0"));

        Assert.Equal("1.0.0", offered);
    }

    [Fact]
    public void A_release_marked_as_a_pre_release_is_skipped_for_a_stable_install_whatever_its_version_looks_like()
    {
        Assert.Null(Newest("1.0.0", Release("1.1.0", preRelease: true)));
    }

    [Fact]
    public void A_version_with_a_pre_release_part_is_skipped_for_a_stable_install_even_when_not_marked()
    {
        Assert.Null(Newest("1.0.0", Release("1.1.0-rc.1")));
    }

    [Fact]
    public void A_draft_is_never_offered()
    {
        var offered = Newest("1.0.0-rc.1", Release("1.0.0-rc.2", preRelease: true, draft: true), Release("1.0.0-rc.1", preRelease: true));

        Assert.Equal("1.0.0-rc.1", offered);
    }

    [Fact]
    public void The_newest_is_picked_by_version_not_by_list_order()
    {
        Assert.Equal("2.0.0", Newest("1.0.0", Release("1.5.0"), Release("2.0.0"), Release("1.9.0")));
    }

    [Fact]
    public void A_tag_that_is_not_a_version_is_skipped()
    {
        Assert.Equal("1.0.0", Newest("0.9.0", Release("nightly"), Release("1.0.0")));
    }

    [Fact]
    public void Nothing_published_offers_nothing()
    {
        Assert.Null(Newest("1.0.0"));
    }

    [Fact]
    public void A_running_version_that_is_not_a_version_is_treated_as_stable()
    {
        Assert.Equal("1.0.0", Newest("local build", Release("1.0.1-rc.1", preRelease: true), Release("1.0.0")));
    }
}
