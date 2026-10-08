using System.Globalization;

namespace Weir.E2E.Tests.Harness;

/// <summary>
/// The Windows tray's side of its hand-off with the server. The tray is a separate program the browser tests do not start, and
/// the only thing it does to tell the server an update was downloaded is write a small file in Weir's data folder, so that
/// file is written here, whole and renamed into place as the tray does it. Nothing in the database is touched.
/// </summary>
public static class TrayFiles
{
    private const string UpdateState = "update-state.json";

    public static void WriteUpdateState(string home, bool downloaded, string version)
    {
        var scratch = Path.Join(home, $".{UpdateState}.{Guid.NewGuid():N}.tmp");
        var downloadedText = downloaded ? "true" : "false";
        File.WriteAllText(scratch, string.Create(CultureInfo.InvariantCulture, $"{{\"downloaded\": {downloadedText}, \"version\": \"{version}\"}}"));
        File.Move(scratch, Path.Join(home, UpdateState), overwrite: true);
    }

    /// <summary>The tray has installed the update: the file that said one was waiting is gone.</summary>
    public static void ClearUpdateState(string home) => File.Delete(Path.Join(home, UpdateState));
}
