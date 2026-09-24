using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// SABnzbd's <c>GET /api?mode=get_config</c> JSON, read with no I/O (#768). <c>section=misc</c> answers
/// <c>{"config":{"misc":{"complete_dir":"..."}}}</c>; <c>section=categories</c> answers
/// <c>{"config":{"categories":[{"name":"...","dir":"..."},...]}}</c>.
/// </summary>
public static class SabnzbdRules
{
    /// <summary>The base completed-downloads folder, from <c>section=misc</c>.</summary>
    public static string? ParseCompleteDir(PyJson? miscConfig) =>
        Config(miscConfig)?.Get("misc") is PyDict misc ? PyValues.Text(misc.Get("complete_dir")) : null;

    /// <summary>
    /// Each category's own folder, from <c>section=categories</c>. A category's <c>dir</c> is relative to
    /// <paramref name="completeDir"/> unless it already looks absolute, in which case it replaces it outright.
    /// The default category (<c>"*"</c>) is not a folder suggestion of its own — its downloads are already
    /// covered by <see cref="ParseCompleteDir"/>.
    /// </summary>
    public static List<DownloadClientCategoryFolder> ParseCategoryFolders(PyJson? categoriesConfig, string? completeDir)
    {
        if (Config(categoriesConfig)?.Get("categories") is not PyList categories)
        {
            return [];
        }

        var folders = new List<DownloadClientCategoryFolder>();
        foreach (var row in categories.Items.OfType<PyDict>())
        {
            var name = PyValues.Text(row.Get("name"));
            var dir = PyValues.Text(row.Get("dir"));
            if (name is null || dir is null || name == "*")
            {
                continue;
            }

            folders.Add(new DownloadClientCategoryFolder(name, JoinUnderComplete(completeDir, dir)));
        }

        return folders;
    }

    private static PyDict? Config(PyJson? payload) => payload is PyDict dict && dict.Get("config") is PyDict config ? config : null;

    private static string JoinUnderComplete(string? completeDir, string relativeOrAbsolute) =>
        DownloadClientPaths.IsAbsolute(relativeOrAbsolute) || string.IsNullOrEmpty(completeDir)
            ? relativeOrAbsolute
            : DownloadClientPaths.Join(completeDir, relativeOrAbsolute);
}
