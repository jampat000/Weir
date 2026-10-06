namespace Weir.Contract.Tests.Auth;

/// <summary>A folder shaped like the web app's build output (<c>index.html</c> and an <c>assets</c> folder), for <c>WEIR_WEB_DIST</c>.</summary>
public static class WebDist
{
    public const string DefaultIndex = "<!doctype html><div id='root'></div>";

    /// <summary>
    /// Writes <c>dist/index.html</c> and <c>dist/assets/*</c> under a new temporary folder and returns the <c>dist</c> path.
    /// The caller deletes the parent folder when done.
    /// </summary>
    public static string Write(string indexHtml, IReadOnlyDictionary<string, byte[]>? assets = null)
    {
        var dist = Path.Combine(Directory.CreateTempSubdirectory("weir_contract_web_").FullName, "dist");
        Directory.CreateDirectory(dist);
        File.WriteAllText(Path.Combine(dist, "index.html"), indexHtml);
        if (assets is { Count: > 0 })
        {
            var folder = Path.Combine(dist, "assets");
            Directory.CreateDirectory(folder);
            foreach (var (name, bytes) in assets)
            {
                File.WriteAllBytes(Path.Combine(folder, name), bytes);
            }
        }

        return dist;
    }
}
