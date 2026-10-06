using System.Net;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;
using static Weir.Contract.Tests.Libraries.LibraryRequests;

namespace Weir.Contract.Tests.Libraries;

// The wait, minimum size, writer, free space and files at once of a workflow (the second half of test_processing_libraries_api.py).
public sealed partial class ProcessingLibrariesApiTests
{
    private const string OperatorSettings = Api + "/processing/operator-settings";

    // --- the wait and minimum size live on the workflow (Performance holds neither) --------------------

    [Fact]
    public async Task A_new_workflow_starts_at_sixty_seconds_and_fifty_megabytes()
    {
        using var operatorClient = await OperatorAsync();
        var created = (await Create(operatorClient)).Fields;

        Assert.Equal(60, (int)created["ready_after_seconds"]!);
        Assert.Equal(50, (int)created["min_file_size_mb"]!);
        foreach (var retired in new[]
        {
            "min_file_age_seconds",
            "hold_minutes",
            "file_detection_interval_seconds",
            "effective_min_file_size_mb",
            "effective_min_file_age_seconds",
        })
        {
            Assert.False(created.ContainsKey(retired), retired);
        }
    }

    [Fact]
    public async Task A_workflow_keeps_the_wait_and_minimum_size_it_was_given()
    {
        using var operatorClient = await OperatorAsync();
        var created = (await Create(operatorClient, ("ready_after_seconds", 0), ("min_file_size_mb", 5))).Fields;

        var stored = (await operatorClient.GetAsync($"{LibrariesUrl}/{(long)created["id"]!}")).Fields;
        Assert.Equal(0, (int)stored["ready_after_seconds"]!);
        Assert.Equal(5, (int)stored["min_file_size_mb"]!);
    }

    [Fact]
    public async Task The_seeded_workflows_hold_a_wait_and_minimum_size_of_their_own()
    {
        using var operatorClient = await OperatorAsync();

        foreach (var row in (await operatorClient.GetAsync(LibrariesUrl)).Elements)
        {
            Assert.True(IsInteger(row!["ready_after_seconds"]));
            Assert.True(IsInteger(row["min_file_size_mb"]));
        }
    }

    [Fact]
    public async Task Performance_settings_do_not_change_a_workflow()
    {
        using var operatorClient = await OperatorAsync();
        var created = (await Create(operatorClient)).Fields;

        await operatorClient.PutWithCsrfAsync(OperatorSettings, Obj(("min_file_age_seconds", 120), ("min_input_file_size_mb", 200)));

        var stored = (await operatorClient.GetAsync($"{LibrariesUrl}/{(long)created["id"]!}")).Fields;
        Assert.Equal(60, (int)stored["ready_after_seconds"]!);
        Assert.Equal(50, (int)stored["min_file_size_mb"]!);
    }

    [Fact]
    public async Task An_older_client_sending_the_three_waits_is_answered_and_they_are_ignored()
    {
        using var operatorClient = await OperatorAsync();

        var created = (await Create(
            operatorClient,
            ("min_file_age_seconds", 5),
            ("hold_minutes", 10),
            ("file_detection_interval_seconds", 999))).Fields;

        Assert.Equal(60, (int)created["ready_after_seconds"]!);
    }

    // --- which tool writes the file is the workflow's own ----------------------------------------------

    [Fact]
    public async Task A_workflow_keeps_the_writer_it_was_given()
    {
        using var operatorClient = await OperatorAsync();
        var created = (await Create(operatorClient)).Fields;
        Assert.Equal("best", (string)created["remux_writer"]!);

        var own = await Create(
            operatorClient,
            ("name", "Movies ffmpeg"),
            ("watched_folder", "/srv/ff/in"),
            ("output_folder", "/srv/ff/out"),
            ("remux_writer", "ffmpeg"));

        own.ShouldBe(HttpStatusCode.Created);
        Assert.Equal("ffmpeg", (string)own.Fields["remux_writer"]!);
        Assert.Equal("ffmpeg", (string)(await operatorClient.GetAsync($"{LibrariesUrl}/{(long)own.Fields["id"]!}")).Fields["remux_writer"]!);
    }

    // --- the space to keep free, and the most files at once, belong to each workflow ------------------

    [Fact]
    public async Task A_workflow_keeps_five_gigabytes_free_unless_it_says_otherwise()
    {
        using var operatorClient = await OperatorAsync();
        var created = (await Create(operatorClient)).Fields;
        Assert.Equal(5120, (int)created["minimum_free_disk_space_mb"]!);

        var own = (await Create(
            operatorClient,
            ("name", "Movies own"),
            ("watched_folder", "/srv/own/in"),
            ("output_folder", "/srv/own/out"),
            ("minimum_free_disk_space_mb", 20480))).Fields;
        Assert.Equal(20480, (int)own["minimum_free_disk_space_mb"]!);
        var stored = (await operatorClient.GetAsync($"{LibrariesUrl}/{(long)own["id"]!}")).Fields;
        Assert.Equal(20480, (int)stored["minimum_free_disk_space_mb"]!);
    }

    [Fact]
    public async Task Performance_accepts_the_retired_free_space_field_without_acting_on_it()
    {
        using var operatorClient = await OperatorAsync();
        var workflow = (await Create(operatorClient, ("minimum_free_disk_space_mb", 2048))).Fields;

        var saved = await operatorClient.PutWithCsrfAsync(
            OperatorSettings, Obj(("minimum_free_disk_space_mb", 999), ("runner_cost_undetermined", 9)));

        saved.ShouldBe(HttpStatusCode.OK);
        Assert.False(saved.Fields.ContainsKey("minimum_free_disk_space_mb"));
        var stored = (await operatorClient.GetAsync($"{LibrariesUrl}/{(long)workflow["id"]!}")).Fields;
        Assert.Equal(2048, (int)stored["minimum_free_disk_space_mb"]!);
    }

    [Fact]
    public async Task A_workflow_cannot_ask_for_more_at_once_than_performance_runs_in_total()
    {
        using var operatorClient = await OperatorAsync();
        var before = (int)(await operatorClient.GetAsync(OperatorSettings)).Fields["max_concurrent_files"]!;
        (await operatorClient.PutWithCsrfAsync(OperatorSettings, Obj(("max_concurrent_files", 2)))).ShouldBe(HttpStatusCode.OK);
        try
        {
            var within = await Create(operatorClient, ("max_concurrent_files", 2));
            within.ShouldBe(HttpStatusCode.Created);

            var refused = await Create(
                operatorClient,
                ("name", "Movies more"),
                ("watched_folder", "/srv/more/in"),
                ("output_folder", "/srv/more/out"),
                ("max_concurrent_files", 3));
            refused.ShouldBe(HttpStatusCode.BadRequest);
            Assert.Contains("cannot be more than the 2 Weir runs in total", (string)refused.Fields["detail"]!);
        }
        finally
        {
            await operatorClient.PutWithCsrfAsync(OperatorSettings, Obj(("max_concurrent_files", before)));
        }
    }
}
