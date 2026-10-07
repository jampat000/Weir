using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="ManagerSetupRules.EvaluateDeluno"/>: what Deluno's manifest can and cannot tell a library about where its downloads land.</summary>
public sealed class ManagerSetupRulesDelunoTests
{
    private static readonly ManagerLibraryDescriptor RefiningTv =
        new("tv-1", "TV", MediaManagerKinds.Tv, "/media/tv", "/media/downloads/weir/tv", true, "/media/downloads/complete/tv");

    [Fact]
    public void A_deluno_library_that_hands_files_over_is_right_when_its_folders_match_and_suggests_them_either_way()
    {
        var right = ManagerSetupRules.EvaluateDeluno("Deluno", MediaManagerKinds.Tv, "/media/downloads/complete", "/media/downloads/weir/tv", [RefiningTv]);
        var wrong = ManagerSetupRules.EvaluateDeluno("Deluno", MediaManagerKinds.Tv, "/media/tv", "/elsewhere", [RefiningTv]);

        Assert.Equal(("/media/downloads/complete/tv", "/media/downloads/weir/tv"), (right.WatchedFolder, right.OutputFolder));
        Assert.DoesNotContain(right.Lines, line => line.State == SetupCheckLine.Problem);
        Assert.Equal(("/media/downloads/complete/tv", "/media/downloads/weir/tv"), (wrong.WatchedFolder, wrong.OutputFolder));
    }

    private sealed class LinkedFolders(Dictionary<string, string> finalPaths) : IFolderProbe
    {
        public bool Exists(string path) => true;

        public bool CanRead(string path) => true;

        public bool CanWrite(string path) => true;

        public string? ResolveFinalPath(string path) => finalPaths.GetValueOrDefault(path);

        public bool? SameFilesystem(string first, string second) => true;
    }

    private const string Mappings = "Settings › Media Management › Processing Workflow › Weir › Path mappings";

    private static readonly ManagerLibraryDescriptor RefiningMovies =
        new("movies-1", "Movies", MediaManagerKinds.Movie, "C:\\Media\\Movies", "C:\\Weir\\Ready\\Movies", true, "C:\\NasMount\\Completed\\Movies");

    private static SetupCheckLine LineAbout(DelunoSetupResult result, string text) =>
        Assert.Single(result.Lines, line => line.Text.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void A_downloads_folder_outside_the_watched_folder_is_unverified_and_names_both_ways_to_fix_it()
    {
        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Movie, "C:\\Downloads\\Completed\\Movies", "C:\\Weir\\Ready\\Movies", [RefiningMovies]);

        var line = LineAbout(result, "reports Movies");
        Assert.Equal(SetupCheckLine.Unverified, line.State);
        Assert.Equal(
            "Deluno reports Movies's downloads in C:\\NasMount\\Completed\\Movies, which isn't inside this workflow's watched folder C:\\Downloads\\Completed\\Movies as Weir sees it. " +
            $"If Deluno has a path mapping from C:\\NasMount to C:\\Downloads ({Mappings}), this is fine. " +
            "Otherwise set the watched folder to C:\\NasMount\\Completed\\Movies.",
            line.Text);
    }

    [Fact]
    public void Paths_that_share_no_trailing_folder_are_named_whole_in_the_suggested_mapping()
    {
        var result = ManagerSetupRules.EvaluateDeluno("Deluno", MediaManagerKinds.Tv, "/media/films", "/media/downloads/weir/tv", [RefiningTv]);

        Assert.Equal(
            "Deluno reports TV's downloads in /media/downloads/complete/tv, which isn't inside this workflow's watched folder /media/films as Weir sees it. " +
            $"If Deluno has a path mapping from /media/downloads/complete/tv to /media/films ({Mappings}), this is fine. " +
            "Otherwise set the watched folder to /media/downloads/complete/tv.",
            LineAbout(result, "reports TV").Text);
    }

    [Fact]
    public void A_downloads_folder_that_is_the_watched_folder_reached_through_a_link_is_not_flagged()
    {
        var links = new LinkedFolders(new()
        {
            ["C:\\NasMount\\Completed\\Movies"] = "C:\\Downloads\\Completed\\Movies",
            ["C:\\Downloads\\Completed\\Movies"] = "C:\\Downloads\\Completed\\Movies",
        });

        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Movie, "C:\\Downloads\\Completed\\Movies", "C:\\Weir\\Ready\\Movies", [RefiningMovies], probe: links);

        var line = LineAbout(result, "downloads to");
        Assert.Equal(SetupCheckLine.Unverified, line.State);
        Assert.Equal(
            "Deluno says its Movies library downloads to C:\\NasMount\\Completed\\Movies, which leads into Weir's watched folder C:\\Downloads\\Completed\\Movies. Weir can't see where each download client really saves.",
            line.Text);
        Assert.DoesNotContain(result.Lines, other => other.Text.Contains("isn't inside", StringComparison.Ordinal));
    }

    [Fact]
    public void A_downloads_folder_that_cannot_be_resolved_falls_back_to_the_folders_as_typed()
    {
        var onlyWatchedResolves = new LinkedFolders(new() { ["C:\\Downloads\\Completed\\Movies"] = "C:\\Downloads\\Completed\\Movies" });

        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Movie, "C:\\Downloads\\Completed\\Movies", "C:\\Weir\\Ready\\Movies", [RefiningMovies], probe: onlyWatchedResolves);

        Assert.Equal(SetupCheckLine.Unverified, LineAbout(result, "isn't inside this workflow's watched folder").State);
    }

    [Fact]
    public void Two_folders_that_resolve_to_different_places_stay_different()
    {
        var links = new LinkedFolders(new()
        {
            ["C:\\NasMount\\Completed\\Movies"] = "D:\\Elsewhere",
            ["C:\\Downloads\\Completed\\Movies"] = "C:\\Downloads\\Completed\\Movies",
        });

        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Movie, "C:\\Downloads\\Completed\\Movies", "C:\\Weir\\Ready\\Movies", [RefiningMovies], probe: links);

        LineAbout(result, "isn't inside this workflow's watched folder");
    }

    [Fact]
    public void A_downloads_folder_with_no_watched_folder_to_compare_is_a_problem_naming_the_folder_to_set()
    {
        var result = ManagerSetupRules.EvaluateDeluno("Deluno", MediaManagerKinds.Tv, string.Empty, "/media/downloads/weir/tv", [RefiningTv]);

        var line = Assert.Single(result.Lines, line => line.State == SetupCheckLine.Problem);
        Assert.Equal(
            "Deluno reports TV's downloads in /media/downloads/complete/tv, but this workflow has no watched folder. Set the watched folder to /media/downloads/complete/tv.",
            line.Text);
    }

    [Fact]
    public void A_different_output_folder_is_unverified_and_names_both_ways_to_fix_it()
    {
        var result = ManagerSetupRules.EvaluateDeluno("Deluno", MediaManagerKinds.Tv, "/media/downloads/complete", "/elsewhere/tv", [RefiningTv]);

        var line = LineAbout(result, "picks up cleaned files");
        Assert.Equal(SetupCheckLine.Unverified, line.State);
        Assert.Equal(
            "Deluno picks up cleaned files from /media/downloads/weir/tv, which isn't this workflow's output folder /elsewhere/tv as Weir sees it. " +
            $"If Deluno has a path mapping from /media/downloads/weir to /elsewhere ({Mappings}), this is fine. " +
            "Otherwise set the output folder to /media/downloads/weir/tv.",
            line.Text);
    }

    [Fact]
    public void An_output_folder_that_is_the_same_folder_reached_through_a_link_is_fine()
    {
        var links = new LinkedFolders(new()
        {
            ["C:\\NasMount\\Ready"] = "C:\\Weir\\Ready",
            ["C:\\Weir\\Ready"] = "C:\\Weir\\Ready",
        });

        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Movie, "C:\\NasMount\\Completed\\Movies", "C:\\Weir\\Ready", [RefiningMovies with { OutputPath = "C:\\NasMount\\Ready" }], probe: links);

        Assert.Equal(SetupCheckLine.Ok, LineAbout(result, "picks up cleaned files").State);
    }

    [Fact]
    public void A_deluno_output_folder_with_no_output_folder_to_compare_is_a_problem_naming_the_folder_to_set()
    {
        var result = ManagerSetupRules.EvaluateDeluno("Deluno", MediaManagerKinds.Tv, "/media/downloads/complete", string.Empty, [RefiningTv]);

        var line = Assert.Single(result.Lines, line => line.State == SetupCheckLine.Problem);
        Assert.Equal(
            "Deluno picks up cleaned files from /media/downloads/weir/tv, but this workflow has no output folder. Set the output folder to /media/downloads/weir/tv.",
            line.Text);
    }

    [Fact]
    public void A_declared_downloads_folder_inside_the_watched_folder_is_unverified_never_ok()
    {
        var result = ManagerSetupRules.EvaluateDeluno("Deluno", MediaManagerKinds.Tv, "/media/downloads/complete", "/media/downloads/weir/tv", [RefiningTv]);

        Assert.Equal(
            "Deluno says its TV library downloads to /media/downloads/complete/tv (inside Weir's watched folder). Weir can't see where each download client really saves.",
            Assert.Single(result.Lines, line => line.State == SetupCheckLine.Unverified).Text);
        Assert.DoesNotContain(result.Lines, line => line.Text.Contains("downloads arrive", StringComparison.Ordinal) && line.State == SetupCheckLine.Ok);
    }

    [Fact]
    public void A_deluno_that_does_not_say_where_downloads_arrive_is_unverified()
    {
        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Tv, "/media/downloads/complete", "/media/downloads/weir/tv", [RefiningTv with { DownloadsPath = null }]);

        Assert.Equal(
            "Deluno does not say where TV's downloads arrive, so Weir cannot verify that its hand-offs sit inside this workflow's watched folder. " +
            "Set the downloads folder in Deluno (or the clients' category folders) and Weir will pick it up.",
            Assert.Single(result.Lines, line => line.State == SetupCheckLine.Unverified).Text);
    }

    [Fact]
    public void Each_enabled_deluno_download_client_gets_an_unverified_line_naming_its_category()
    {
        ManagerDownloadClientDescriptor[] clients =
        [
            new("Transmission", true, "deluno-movies", "deluno-tv"),
            new("qBittorrent", true, "deluno-movies", "deluno-tv"),
            new("Old SABnzbd", false, "deluno-movies", "deluno-tv"),
        ];

        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Tv, "/media/downloads/complete", "/media/downloads/weir/tv", [RefiningTv], clients);

        Assert.Equal(
            [
                "Deluno's Transmission files this workflow's downloads under the category \"deluno-tv\", but Deluno does not publish where it saves them. Weir cannot verify they land inside the watched folder.",
                "Deluno's qBittorrent files this workflow's downloads under the category \"deluno-tv\", but Deluno does not publish where it saves them. Weir cannot verify they land inside the watched folder.",
            ],
            result.Lines.Where(line => line.Text.Contains("files this workflow's downloads", StringComparison.Ordinal)).Select(line => line.Text));
        Assert.DoesNotContain(result.Lines, line => line.State == SetupCheckLine.Ok && line.Text.Contains("files this workflow's downloads", StringComparison.Ordinal));
    }

    [Fact]
    public void A_declared_downloads_folder_outside_the_watched_folder_is_never_a_pass_whatever_its_clients_say()
    {
        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Tv, "/media/tv", "/media/downloads/weir/tv", [RefiningTv], [new("Transmission", true, null, "deluno-tv")]);

        Assert.Equal(SetupCheckLine.Unverified, LineAbout(result, "isn't inside this workflow's watched folder").State);
        Assert.DoesNotContain(result.Lines, line => line.State == SetupCheckLine.Problem);
    }

    [Fact]
    public void A_deluno_without_a_library_set_to_refine_before_import_will_never_hand_files_over()
    {
        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Tv, "/media/downloads/complete", "/media/downloads/weir", [RefiningTv with { ProcessesBeforeImport = false }, RefiningTv with { MediaScope = MediaManagerKinds.Movie }]);

        Assert.Equal(
            "No Deluno TV library is set to Refine before import, so Deluno will not hand TV downloads to Weir. Choose Refine before import for that library in Deluno.",
            Assert.Single(result.Lines).Text);
        Assert.Null(result.WatchedFolder);
    }

    [Fact]
    public void Deluno_manifests_carry_each_download_clients_name_and_categories_but_no_folder()
    {
        var clients = ManagerDialectRules.ManifestDownloadClients(WireJsonParser.Parse(
            """{"downloadClients":[{"id":"c1","name":"Transmission","protocol":"torrent","moviesCategory":"deluno-movies","tvCategory":"deluno-tv","isEnabled":true},{"id":"c2","name":"Off","isEnabled":false}]}"""));

        Assert.Equal(
            [new ManagerDownloadClientDescriptor("Transmission", true, "deluno-movies", "deluno-tv"), new ManagerDownloadClientDescriptor("Off", false, null, null)],
            clients);
    }

    [Fact]
    public void Deluno_manifests_carry_the_downloads_folder_beside_the_library_root()
    {
        var descriptor = ManagerDialectRules.ManifestLibraryDescriptor((WireObject)WireJsonParser.Parse(
            """{"id":"lib-1","name":"TV","mediaType":"tv","rootPath":"/media/tv","downloadsPath":"/media/downloads/complete/tv","importWorkflow":"refine-before-import","processorOutputPath":"/media/downloads/weir/tv"}"""));

        Assert.Equal(RefiningTv with { Key = "lib-1" }, descriptor);
    }
}
