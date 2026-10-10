using System.Net;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;
using Weir.Contract.Tests.Processing;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>
/// A hand-off that lists its <c>sourceFiles</c> means those files and nothing else in the release folder: Deluno lists only the files at
/// or above the workflow's minimum size, so a small extra it leaves out stays untouched in the download folder for seeding.
/// </summary>
[ContractArea("media_managers")]
public sealed class HandoffSourceFilesTests
{
    private const string Release = "Film.2020";
    private const string Main = "film.mkv";
    private const string Extra = "Gallery.mkv";
    private const int MinimumMb = 1;

    [Fact]
    public async Task A_small_extra_the_hand_off_does_not_list_is_never_seen_reported_or_touched()
    {
        await using var scenario = await Scenario.StartAsync();
        var (fake, library) = await scenario.DelunoSetupAsync(library: [("min_file_size_mb", MinimumMb)]);
        var film = scenario.WriteRelease(Release, Main, FakeMedia.Bytes(FakeMedia.Probe(audioLanguages: ["eng", "fre"]), padding: 2 * 1024 * 1024));
        var extra = scenario.WriteRelease(Release, Extra, FakeMedia.Bytes(FakeMedia.Probe()));
        var extraBytes = await File.ReadAllBytesAsync(extra);

        await scenario.PostHandoffAsync("listed-1", Path.Combine(scenario.Folders.Watched, Release), sourceFiles: [film]);

        var status = await scenario.WaitForHandoffStateAsync("listed-1", "completed");
        Assert.True(File.Exists(Path.Combine(scenario.Folders.Output, Release, Main)));
        Assert.False(File.Exists(Path.Combine(scenario.Folders.Output, Release, Extra)));

        await fake.WaitForRequestAsync("POST", Scenario.EventsPath);
        var report = Assert.Single(Scenario.Callbacks(fake, "listed-1"));
        Assert.Equal("completed", (string)report["status"]!);
        Assert.Equal(
            [$"{Scenario.DelunoOutputRoot}/{Release}/{Main}"],
            report["outputFiles"]!.AsArray().Select(file => ((string)file!).Replace('\\', '/')));
        Assert.DoesNotContain(Extra, report.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Extra, status.ToJsonString(), StringComparison.Ordinal);

        var activity = await scenario.Admin.GetAsync($"{WeirClient.Api}/activity/recent", ("limit", 100));
        Assert.Equal(HttpStatusCode.OK, activity.Status);
        Assert.Contains(Main, activity.Fields.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Extra, activity.Fields.ToJsonString(), StringComparison.Ordinal);
        Assert.Null(await scenario.FileRowAsync(library, $"{Release}/{Extra}"));

        Assert.Equal(extraBytes, await File.ReadAllBytesAsync(extra));
    }
}
