using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="ManagerSourceFactsRules"/>: the names a manager uses for where a workflow's files come from and go to.</summary>
public sealed class ManagerSourceFactsRulesTests
{
    private static ManagerLibraryDescriptor Library(string name, string scope, bool refines = true) =>
        new("k-" + name, name, scope, ProcessesBeforeImport: refines);

    [Fact]
    public void Deluno_reports_the_library_that_refines_this_media_type_and_its_download_clients_category()
    {
        var facts = ManagerSourceFactsRules.ForDeluno(
            "movie",
            [Library("Films", "movie"), Library("Shows", "tv")],
            [new ManagerDownloadClientDescriptor("qBittorrent", true, "deluno-movies", "deluno-tv")]);

        Assert.Equal(new ManagerSourceFacts("deluno-movies", "Films", null), facts);
    }

    [Fact]
    public void Deluno_uses_the_tv_category_for_tv()
    {
        var facts = ManagerSourceFactsRules.ForDeluno(
            "tv",
            [Library("Shows", "tv")],
            [new ManagerDownloadClientDescriptor("qBittorrent", true, "deluno-movies", "deluno-tv")]);

        Assert.Equal("deluno-tv", facts.Category);
    }

    [Fact]
    public void A_disabled_download_client_names_no_category()
    {
        var facts = ManagerSourceFactsRules.ForDeluno(
            "movie", [Library("Films", "movie")], [new ManagerDownloadClientDescriptor("qBittorrent", false, "deluno-movies", null)]);

        Assert.Null(facts.Category);
    }

    [Fact]
    public void A_library_that_does_not_refine_before_import_is_not_named()
    {
        var facts = ManagerSourceFactsRules.ForDeluno("movie", [Library("Films", "movie", refines: false)], null);

        Assert.Equal(ManagerSourceFacts.None, facts);
    }

    [Fact]
    public void Arr_reports_the_first_enabled_clients_category_and_the_first_root_folder()
    {
        var facts = ManagerSourceFactsRules.ForArr(
            [
                new ArrDownloadClientEntry("Old", "QBittorrent", false, "old", "ignored", null),
                new ArrDownloadClientEntry("qBittorrent", "QBittorrent", true, "qbittorrent", "radarr", null),
            ],
            ["Z:\\Movies", "Y:\\Movies 4K"]);

        Assert.Equal(new ManagerSourceFacts("radarr", null, "Z:\\Movies"), facts);
    }

    [Fact]
    public void Arr_with_nothing_reported_names_nothing()
    {
        Assert.Equal(ManagerSourceFacts.None, ManagerSourceFactsRules.ForArr([], []));
    }
}
