using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// Transmission's RPC <c>session-get</c> answer, read with no I/O (#768):
/// <c>{"arguments":{"download-dir":"..."},"result":"success"}</c>. Transmission has no category concept, so this
/// is the only folder it ever reports.
/// </summary>
public static class TransmissionRules
{
    public static DownloadClientFolders Parse(WireValue? sessionGetResponse)
    {
        var downloadDir = sessionGetResponse is WireObject dict && dict.Get("arguments") is WireObject arguments
            ? ManagerValues.Text(arguments.Get("download-dir"))
            : null;
        return new DownloadClientFolders(downloadDir, []);
    }
}
