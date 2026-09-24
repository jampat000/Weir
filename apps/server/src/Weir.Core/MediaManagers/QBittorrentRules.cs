using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// qBittorrent's <c>GET /api/v2/torrents/categories</c> (an object keyed by category name, each
/// <c>{"name": "...", "savePath": "..."}</c>) and <c>GET /api/v2/app/preferences</c> (<c>save_path</c> is the
/// default when a torrent has no category, or a category's own <c>savePath</c> is empty), read with no I/O (#768).
/// </summary>
public static class QBittorrentRules
{
    public static DownloadClientFolders Parse(WireValue? categoriesResponse, WireValue? preferencesResponse)
    {
        var savePath = preferencesResponse is WireObject preferences ? ManagerValues.Text(preferences.Get("save_path")) : null;
        var categories = new List<DownloadClientCategoryFolder>();
        if (categoriesResponse is WireObject dict)
        {
            foreach (var (name, value) in dict.Items)
            {
                if (value is WireObject row && ManagerValues.Text(row.Get("savePath")) is { } folder)
                {
                    categories.Add(new DownloadClientCategoryFolder(name, folder));
                }
            }
        }

        return new DownloadClientFolders(savePath, [.. categories.OrderBy(category => category.Category, StringComparer.Ordinal)]);
    }
}
