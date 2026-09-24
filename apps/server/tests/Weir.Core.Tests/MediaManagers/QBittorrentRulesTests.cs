using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="QBittorrentRules"/>: <c>GET /api/v2/torrents/categories</c> and <c>GET /api/v2/app/preferences</c> (#768).</summary>
public sealed class QBittorrentRulesTests
{
    private const string Categories = """
        {"tv-sonarr":{"name":"tv-sonarr","savePath":"/downloads/complete/tv"},"movies":{"name":"movies","savePath":""}}
        """;

    private const string Preferences = """{"save_path":"/downloads/complete","other_setting":true}""";

    [Fact]
    public void The_default_folder_is_the_preferences_save_path()
    {
        var folders = QBittorrentRules.Parse(PyJsonParser.Parse(Categories), PyJsonParser.Parse(Preferences));
        Assert.Equal("/downloads/complete", folders.CompletedFolder);
    }

    [Fact]
    public void A_category_with_its_own_save_path_is_reported()
    {
        var folders = QBittorrentRules.Parse(PyJsonParser.Parse(Categories), PyJsonParser.Parse(Preferences));
        Assert.Contains(new DownloadClientCategoryFolder("tv-sonarr", "/downloads/complete/tv"), folders.CategoryFolders);
    }

    [Fact]
    public void A_category_with_an_empty_save_path_is_not_reported_as_a_folder_of_its_own()
    {
        var folders = QBittorrentRules.Parse(PyJsonParser.Parse(Categories), PyJsonParser.Parse(Preferences));
        Assert.DoesNotContain(folders.CategoryFolders, category => category.Category == "movies");
    }

    [Fact]
    public void No_categories_object_yields_an_empty_category_list()
    {
        var folders = QBittorrentRules.Parse(null, PyJsonParser.Parse(Preferences));
        Assert.Empty(folders.CategoryFolders);
        Assert.Equal("/downloads/complete", folders.CompletedFolder);
    }
}
