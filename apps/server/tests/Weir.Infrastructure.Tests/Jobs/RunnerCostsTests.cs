using Weir.Infrastructure.Jobs;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>
/// What a job costs against the resolution budget: every job that reads a whole video is costed by that video's resolution,
/// a file of unknown resolution costs what a 1080p file does, and a running job is corrected once its resolution is measured.
/// </summary>
public sealed class RunnerCostsTests : IDisposable
{
    private const string Remux = "processing.file.remux_pass.v1";
    private const string Scan = "processing.watched_folder.remux_scan_dispatch.v1";
    private const int Sd = 1;
    private const int P720 = 2;
    private const int P1080 = 3;
    private const int FourK = 5;

    private readonly JobsTestDatabase _db = new();
    private readonly long _library;

    public RunnerCostsTests()
    {
        _db.Execute(
            "INSERT INTO operator_settings (id, runner_capacity, runner_cost_sd, runner_cost_720p, runner_cost_1080p, runner_cost_4k, runner_budget_enabled) " +
            $"VALUES (1, 12, {Sd}, {P720}, {P1080}, {FourK}, 1)");
        _db.SeedSuiteSettings();
        _library = _db.AddLibrary();
    }

    public void Dispose() => _db.Dispose();

    private string PassPayload(string relativePath) => $$"""{"library_id":{{_library}},"relative_media_path":"{{relativePath}}"}""";

    private void RecordResolution(string relativePath, int width, int height) =>
        _db.Execute(
            "INSERT INTO files (library_id, relative_path, video_width, video_height) VALUES (@library, @path, @width, @height)",
            ("@library", _library), ("@path", relativePath), ("@width", width), ("@height", height));

    private const string FourKProbe =
        """{"streams":[{"index":0,"codec_type":"video","codec_name":"hevc","width":3840,"height":2160}],"format":{}}""";

    [Fact]
    public async Task A_file_pass_costs_the_resolution_recorded_for_its_file()
    {
        RecordResolution("uhd.mkv", 3840, 2160);
        RecordResolution("dvd.mkv", 720, 480);

        var uhd = await _db.Store.EnqueueOrGetAsync("uhd", Remux, PassPayload("uhd.mkv"));
        var dvd = await _db.Store.EnqueueOrGetAsync("dvd", Remux, PassPayload("dvd.mkv"));

        Assert.Equal((FourK, Sd), (uhd.RunnerCost, dvd.RunnerCost));
    }

    [Fact]
    public async Task A_file_pass_for_a_file_of_unknown_resolution_costs_what_a_1080p_file_does()
    {
        var unseen = await _db.Store.EnqueueOrGetAsync("unseen", Remux, PassPayload("unseen.mkv"));
        var unread = await _db.Store.EnqueueOrGetAsync("unread", Remux, "{}");

        Assert.Equal((P1080, P1080), (unseen.RunnerCost, unread.RunnerCost));
    }

    [Fact]
    public async Task A_job_that_reads_no_video_costs_nothing()
    {
        var scan = await _db.Store.EnqueueOrGetAsync("scan", Scan, PassPayload("anything.mkv"));

        Assert.Equal(0, scan.RunnerCost);
    }

    [Fact]
    public async Task A_cost_given_by_the_caller_is_kept()
    {
        var job = await _db.Store.EnqueueOrGetAsync("given", Remux, PassPayload("given.mkv"), runnerCost: 2);

        Assert.Equal(2, job.RunnerCost);
    }

    [Fact]
    public void A_library_clean_costs_the_resolution_its_scan_probed()
    {
        using var connection = _db.Database.Open();
        using var transaction = connection.BeginTransaction();

        var probed = RunnerCosts.ForProbeJson(connection, transaction, FourKProbe);
        var unread = RunnerCosts.ForProbeJson(connection, transaction, null);
        var garbled = RunnerCosts.ForProbeJson(connection, transaction, "not json");

        Assert.Equal((FourK, P1080, P1080), (probed, unread, garbled));
    }

    [Fact]
    public async Task A_running_job_takes_the_cost_of_the_resolution_measured_for_it()
    {
        await _db.Store.EnqueueOrGetAsync("running", Remux, PassPayload("running.mkv"));
        var leased = await _db.Store.ClaimNextAsync("worker", JobsTestDatabase.T0.AddMinutes(5), JobsTestDatabase.T0);
        Assert.Equal(P1080, leased!.RunnerCost);

        Measure(leased.Id, "4k");

        Assert.Equal(FourK, _db.Count("SELECT runner_cost FROM jobs WHERE id = @id", ("@id", leased.Id)));
    }

    [Fact]
    public async Task A_job_that_is_not_running_keeps_its_cost_when_a_resolution_is_measured()
    {
        var waiting = await _db.Store.EnqueueOrGetAsync("waiting", Remux, PassPayload("waiting.mkv"));

        Measure(waiting.Id, "4k");

        Assert.Equal(P1080, _db.Count("SELECT runner_cost FROM jobs WHERE id = @id", ("@id", waiting.Id)));
    }

    [Fact]
    public async Task What_a_running_job_costs_is_counted_by_the_next_admission()
    {
        await _db.Store.EnqueueOrGetAsync("running", Remux, PassPayload("running.mkv"));
        var leased = await _db.Store.ClaimNextAsync("worker", JobsTestDatabase.T0.AddMinutes(5), JobsTestDatabase.T0);
        Measure(leased!.Id, "4k");

        using var connection = _db.Database.Open();
        using var transaction = connection.BeginTransaction();
        var admission = WorkAdmissionReader.Evaluate(connection, transaction, JobsTestDatabase.T0);

        Assert.Equal(12 - FourK, admission.AvailableUnits);
    }

    private void Measure(long jobId, string resolutionClass)
    {
        using var connection = _db.Database.Open();
        using var transaction = connection.BeginTransaction();
        RunnerCosts.RecordMeasured(connection, transaction, jobId, resolutionClass);
        transaction.Commit();
    }
}
