namespace Weir.E2E.Tests.Harness;

/// <summary>Where the repository's built web app and the screenshot artifacts are.</summary>
public static class RepoPaths
{
    private static readonly Lazy<string> Root = new(FindRoot);

    public static string WebDist => Path.Combine(Root.Value, "apps", "web", "dist");

    /// <summary>Informational screenshots, ignored by git: no pixel-exact baselines are committed.</summary>
    public static string Screenshots => Path.Combine(Root.Value, "artifacts", "screenshots");

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "apps", "server", "Weir.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (the folder holding apps/server/Weir.slnx) is not above the test output.");
    }
}
