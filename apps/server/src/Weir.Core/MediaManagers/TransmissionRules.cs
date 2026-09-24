using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// Transmission's RPC <c>session-get</c> answer, read with no I/O (#768):
/// <c>{"arguments":{"download-dir":"..."},"result":"success"}</c>. Transmission has no category concept, so this
/// is the only folder it ever reports.
/// </summary>
public static class TransmissionRules
{
    public static DownloadClientFolders Parse(PyJson? sessionGetResponse)
    {
        var downloadDir = sessionGetResponse is PyDict dict && dict.Get("arguments") is PyDict arguments
            ? PyValues.Text(arguments.Get("download-dir"))
            : null;
        return new DownloadClientFolders(downloadDir, []);
    }
}
