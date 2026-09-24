using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// Deluge Web UI JSON-RPC answers, read with no I/O (#768): <c>core.get_config</c>'s <c>result.move_completed_path</c>
/// is the base completed folder, and <c>label.get_config</c>'s <c>result</c> — an object keyed by label name, each
/// holding that label's own options — gives each label's own <c>move_completed_path</c> when it sets one. A JSON-RPC
/// <c>error</c> answer (the label plugin not being enabled, most often) means no per-label folders, not a failed read.
/// </summary>
public static class DelugeRules
{
    public static DownloadClientFolders Parse(WireValue? coreConfigResponse, WireValue? labelConfigResponse)
    {
        var moveCompletedPath = RpcResult(coreConfigResponse) is { } core ? ManagerValues.Text(core.Get("move_completed_path")) : null;
        var labels = new List<DownloadClientCategoryFolder>();
        if (RpcResult(labelConfigResponse) is { } labelConfig)
        {
            foreach (var (label, value) in labelConfig.Items)
            {
                if (value is WireObject options && ManagerValues.Text(options.Get("move_completed_path")) is { } folder)
                {
                    labels.Add(new DownloadClientCategoryFolder(label, folder));
                }
            }
        }

        return new DownloadClientFolders(moveCompletedPath, [.. labels.OrderBy(label => label.Category, StringComparer.Ordinal)]);
    }

    /// <summary>A JSON-RPC <c>result</c> object, or null when the call answered an <c>error</c> instead.</summary>
    private static WireObject? RpcResult(WireValue? response) =>
        response is WireObject dict && dict.Get("error") is null or WireNull && dict.Get("result") is WireObject result ? result : null;
}
