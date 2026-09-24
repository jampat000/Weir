using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// Suggested watched folders: Weir never changes a library's folders on its own, but it can read a
/// connected manager's own configuration and offer what it finds, for the user to apply with one click.
/// </summary>
public static class WatchFolderSuggestionRules
{
    /// <summary>
    /// The first enabled download client's own directory, when Sonarr or Radarr says one (a client using a shared
    /// default folder plus a category has no absolute path Weir can read from <c>GET /api/v3/downloadclient</c>
    /// alone, so it suggests nothing rather than guess).
    /// </summary>
    public static string? SuggestArrWatchedFolder(IReadOnlyList<ArrDownloadClientEntry> clients)
    {
        ArgumentNullException.ThrowIfNull(clients);
        foreach (var client in clients.Where(client => client.Enabled))
        {
            var directory = new ArrOsPath(WireStrings.Strip(client.Directory ?? string.Empty));
            if (directory.IsRooted)
            {
                return directory.Text;
            }
        }

        return null;
    }
}
