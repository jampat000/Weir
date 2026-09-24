using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="TransmissionRules"/>: RPC <c>session-get</c>'s <c>arguments.download-dir</c> (#768).</summary>
public sealed class TransmissionRulesTests
{
    [Fact]
    public void The_only_folder_is_download_dir()
    {
        var folders = TransmissionRules.Parse(PyJsonParser.Parse("""{"arguments":{"download-dir":"/downloads/complete"},"result":"success"}"""));
        Assert.Equal("/downloads/complete", folders.CompletedFolder);
        Assert.Empty(folders.CategoryFolders);
    }

    [Fact]
    public void No_arguments_yields_no_folder()
    {
        var folders = TransmissionRules.Parse(PyJsonParser.Parse("""{"result":"success"}"""));
        Assert.Null(folders.CompletedFolder);
    }
}
