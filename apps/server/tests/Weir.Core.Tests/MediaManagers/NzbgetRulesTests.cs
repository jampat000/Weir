using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="NzbgetRules"/>: NZBGet's flat <c>{"Name","Value"}</c> option array from its JSON-RPC <c>config</c> method (#768).</summary>
public sealed class NzbgetRulesTests
{
    private const string Config = """
        {"version":"1.1","result":[
          {"Name":"DestDir","Value":"/downloads/complete"},
          {"Name":"Category1.Name","Value":"tv-sonarr"},
          {"Name":"Category1.DestDir","Value":"/downloads/complete/tv"},
          {"Name":"Category2.Name","Value":"movies"},
          {"Name":"Category2.DestDir","Value":"/downloads/complete/movies"},
          {"Name":"Category3.Name","Value":"empty-category"}
        ]}
        """;

    [Fact]
    public void The_base_folder_is_DestDir()
    {
        var folders = NzbgetRules.ParseConfig(PyJsonParser.Parse(Config));
        Assert.Equal("/downloads/complete", folders.CompletedFolder);
    }

    [Fact]
    public void Each_numbered_category_is_paired_with_its_own_destination()
    {
        var folders = NzbgetRules.ParseConfig(PyJsonParser.Parse(Config));
        Assert.Equal(
            [
                new DownloadClientCategoryFolder("tv-sonarr", "/downloads/complete/tv"),
                new DownloadClientCategoryFolder("movies", "/downloads/complete/movies"),
            ],
            folders.CategoryFolders);
    }

    [Fact]
    public void A_category_with_no_destination_is_skipped_rather_than_reported_with_a_blank_folder()
    {
        var folders = NzbgetRules.ParseConfig(PyJsonParser.Parse(Config));
        Assert.DoesNotContain(folders.CategoryFolders, category => category.Category == "empty-category");
    }

    [Fact]
    public void No_result_array_yields_no_folders_at_all()
    {
        var folders = NzbgetRules.ParseConfig(PyJsonParser.Parse("""{"version":"1.1"}"""));
        Assert.Null(folders.CompletedFolder);
        Assert.Empty(folders.CategoryFolders);
    }
}
