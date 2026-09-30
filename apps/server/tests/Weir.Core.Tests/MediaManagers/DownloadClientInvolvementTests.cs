using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary>
/// Which bare download clients a workflow's folder chain is judged against: only those with a reason to feed it.
/// </summary>
public sealed class DownloadClientInvolvementTests
{
    private const string Watched = @"D:\dl\tv";

    private static DownloadClientFolders Folders(string? completed, params string[] categoryFolders) =>
        new(completed, [.. categoryFolders.Select((folder, index) => new DownloadClientCategoryFolder($"category-{index}", folder))]);

    private static ArrDownloadClientEntry ManagerClient(string implementation, string? host, int? port = null, bool enabled = true) =>
        new(implementation, implementation, enabled, host, null, null, null, port);

    [Theory]
    [InlineData(@"D:\dl\tv")]
    [InlineData(@"D:\dl\tv\Show")]
    [InlineData(@"d:\DL\TV\")]
    public void A_client_saving_into_the_watched_folder_or_below_it_feeds_the_workflow(string savesTo)
    {
        Assert.True(DownloadClientInvolvement.SavesInto(Watched, Folders(savesTo)));
    }

    [Fact]
    public void A_client_whose_category_folder_is_the_watched_folder_feeds_the_workflow()
    {
        Assert.True(DownloadClientInvolvement.SavesInto(Watched, Folders(@"D:\dl\complete", @"D:\dl\tv")));
    }

    [Theory]
    [InlineData(@"D:\dl")]
    [InlineData(@"D:\dl\movies")]
    [InlineData(@"D:\dl\tv2")]
    [InlineData("/downloads/tv")]
    public void A_client_saving_elsewhere_or_above_the_watched_folder_does_not_feed_it_on_that_evidence(string savesTo)
    {
        Assert.False(DownloadClientInvolvement.SavesInto(Watched, Folders(savesTo)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("inbox")]
    public void A_workflow_with_no_usable_watched_folder_is_fed_by_no_client(string watched)
    {
        Assert.False(DownloadClientInvolvement.SavesInto(watched, Folders(@"D:\dl\tv")));
    }

    [Fact]
    public void A_client_that_reported_no_folder_does_not_feed_the_workflow_on_that_evidence()
    {
        Assert.False(DownloadClientInvolvement.SavesInto(Watched, DownloadClientFolders.Empty));
    }

    [Fact]
    public void A_client_the_linked_manager_lists_at_the_same_host_and_port_is_used_by_it()
    {
        var used = DownloadClientInvolvement.IsUsedBy("qbittorrent", "http://Seedbox:8080", [ManagerClient("QBittorrent", "seedbox", 8080)]);

        Assert.True(used);
    }

    [Fact]
    public void A_client_written_without_a_port_is_matched_on_the_scheme_s_own_port()
    {
        Assert.True(DownloadClientInvolvement.IsUsedBy("sabnzbd", "https://sab.lan", [ManagerClient("Sabnzbd", "sab.lan", 443)]));
        Assert.False(DownloadClientInvolvement.IsUsedBy("sabnzbd", "https://sab.lan", [ManagerClient("Sabnzbd", "sab.lan", 8080)]));
    }

    [Fact]
    public void A_manager_entry_with_no_port_matches_on_the_host_alone()
    {
        Assert.True(DownloadClientInvolvement.IsUsedBy("deluge", "http://nas:8112", [ManagerClient("Deluge", "nas")]));
    }

    [Theory]
    [InlineData("transmission", "http://seedbox:8080")]
    [InlineData("qbittorrent", "http://other:8080")]
    [InlineData("qbittorrent", "http://seedbox:9090")]
    [InlineData("qbittorrent", "not an address")]
    [InlineData("qbittorrent", "")]
    public void A_client_of_another_product_host_or_port_is_not_the_one_the_manager_uses(string kind, string baseUrl)
    {
        Assert.False(DownloadClientInvolvement.IsUsedBy(kind, baseUrl, [ManagerClient("QBittorrent", "seedbox", 8080)]));
    }

    [Fact]
    public void A_client_the_manager_has_switched_off_is_not_used_by_it()
    {
        Assert.False(DownloadClientInvolvement.IsUsedBy("qbittorrent", "http://seedbox:8080", [ManagerClient("QBittorrent", "seedbox", 8080, enabled: false)]));
    }

    [Fact]
    public void A_manager_that_uses_no_clients_uses_none_of_them()
    {
        Assert.False(DownloadClientInvolvement.IsUsedBy("qbittorrent", "http://seedbox:8080", []));
    }
}
