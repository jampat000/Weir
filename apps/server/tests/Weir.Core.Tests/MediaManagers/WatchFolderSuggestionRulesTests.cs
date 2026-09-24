using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="WatchFolderSuggestionRules"/>: turning a manager's own download client settings into a suggested folder.</summary>
public sealed class WatchFolderSuggestionRulesTests
{
    private static List<ArrDownloadClientEntry> Clients(string json, string scope = MediaManagerKinds.Tv) =>
        ManagerSetupRules.ParseDownloadClients(WireJsonParser.Parse(json), scope);

    [Fact]
    public void An_enabled_clients_own_directory_is_suggested()
    {
        const string json = """
            [{"enable":true,"protocol":"torrent","name":"qBittorrent",
              "fields":[{"name":"host","value":"qbittorrent"},{"name":"tvDirectory","value":"/downloads/tv"}],
              "implementation":"QBittorrent","id":1}]
            """;

        var suggestion = WatchFolderSuggestionRules.SuggestArrWatchedFolder(Clients(json));

        Assert.Equal("/downloads/tv", suggestion);
    }

    [Fact]
    public void A_disabled_clients_directory_is_not_suggested()
    {
        const string json = """
            [{"enable":false,"protocol":"torrent","name":"qBittorrent",
              "fields":[{"name":"host","value":"qbittorrent"},{"name":"tvDirectory","value":"/downloads/tv"}],
              "implementation":"QBittorrent","id":1}]
            """;

        var suggestion = WatchFolderSuggestionRules.SuggestArrWatchedFolder(Clients(json));

        Assert.Null(suggestion);
    }

    [Fact]
    public void A_client_with_only_a_category_and_no_directory_suggests_nothing()
    {
        const string json = """
            [{"enable":true,"protocol":"torrent","name":"qBittorrent",
              "fields":[{"name":"host","value":"qbittorrent"},{"name":"tvCategory","value":"tv-sonarr"}],
              "implementation":"QBittorrent","id":1}]
            """;

        var suggestion = WatchFolderSuggestionRules.SuggestArrWatchedFolder(Clients(json));

        Assert.Null(suggestion);
    }

    [Fact]
    public void The_first_enabled_clients_directory_wins_when_more_than_one_is_set()
    {
        const string json = """
            [{"enable":true,"protocol":"usenet","name":"SABnzbd",
              "fields":[{"name":"host","value":"sabnzbd"},{"name":"tvDirectory","value":"/downloads/nzb-tv"}],
              "implementation":"Sabnzbd","id":1},
             {"enable":true,"protocol":"torrent","name":"qBittorrent",
              "fields":[{"name":"host","value":"qbittorrent"},{"name":"tvDirectory","value":"/downloads/tv"}],
              "implementation":"QBittorrent","id":2}]
            """;

        var suggestion = WatchFolderSuggestionRules.SuggestArrWatchedFolder(Clients(json));

        Assert.Equal("/downloads/nzb-tv", suggestion);
    }
}
