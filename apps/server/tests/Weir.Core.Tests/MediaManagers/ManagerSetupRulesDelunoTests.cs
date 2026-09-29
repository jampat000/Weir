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
        Assert.Equal(
            [
                "TV's downloads arrive in /media/downloads/complete/tv, which is not inside the watched folder, so Weir would refuse its hand-offs. Use Deluno's folders.",
                "Deluno picks up cleaned files from /media/downloads/weir/tv, but this workflow writes to /elsewhere. Unless both are the same folder seen from two machines, use the same folder.",
            ],
            wrong.Lines.Select(line => line.Text));
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
            "Deluno does not say where TV's downloads arrive, so Weir cannot verify that its hand-offs sit inside this workflow's watched folder.",
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
    public void A_declared_downloads_folder_outside_the_watched_folder_stays_a_problem_whatever_its_clients_say()
    {
        var result = ManagerSetupRules.EvaluateDeluno(
            "Deluno", MediaManagerKinds.Tv, "/media/tv", "/media/downloads/weir/tv", [RefiningTv], [new("Transmission", true, null, "deluno-tv")]);

        Assert.Single(result.Lines, line => line.State == SetupCheckLine.Problem && line.Text.Contains("not inside the watched folder", StringComparison.Ordinal));
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
