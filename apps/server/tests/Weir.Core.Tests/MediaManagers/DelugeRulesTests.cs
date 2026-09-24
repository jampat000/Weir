using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="DelugeRules"/>: Deluge Web UI JSON-RPC's <c>core.get_config</c> and <c>label.get_config</c> (#768).</summary>
public sealed class DelugeRulesTests
{
    private const string CoreConfig = """{"result":{"move_completed_path":"/downloads/complete","move_completed":true},"error":null,"id":2}""";

    private const string LabelConfig = """
        {"result":{"tv-sonarr":{"move_completed_path":"/downloads/complete/tv","move_completed":true},"movies":{"move_completed":false}},"error":null,"id":3}
        """;

    private const string LabelPluginDisabled = """{"result":null,"error":{"message":"Unknown method label.get_config","code":8},"id":3}""";

    [Fact]
    public void The_base_folder_is_move_completed_path()
    {
        var folders = DelugeRules.Parse(PyJsonParser.Parse(CoreConfig), PyJsonParser.Parse(LabelConfig));
        Assert.Equal("/downloads/complete", folders.CompletedFolder);
    }

    [Fact]
    public void A_label_with_its_own_move_completed_path_is_reported()
    {
        var folders = DelugeRules.Parse(PyJsonParser.Parse(CoreConfig), PyJsonParser.Parse(LabelConfig));
        Assert.Equal([new DownloadClientCategoryFolder("tv-sonarr", "/downloads/complete/tv")], folders.CategoryFolders);
    }

    [Fact]
    public void A_label_with_no_move_completed_path_of_its_own_is_not_reported()
    {
        var folders = DelugeRules.Parse(PyJsonParser.Parse(CoreConfig), PyJsonParser.Parse(LabelConfig));
        Assert.DoesNotContain(folders.CategoryFolders, label => label.Category == "movies");
    }

    [Fact]
    public void The_label_plugin_being_disabled_means_no_labels_not_a_failed_read()
    {
        var folders = DelugeRules.Parse(PyJsonParser.Parse(CoreConfig), PyJsonParser.Parse(LabelPluginDisabled));
        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        Assert.Empty(folders.CategoryFolders);
    }
}
