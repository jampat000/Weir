using Weir.Core.Jobs;
using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>"Jobs today" on System: the jobs that finished since the day began, and how many of them failed.</summary>
public sealed class FinishedJobsTests : IDisposable
{
    private static readonly DateTimeOffset MidnightLocal = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    private const string RemuxPass = "processing.file.remux_pass.v1";

    private readonly JobsTestDatabase _db = new();
    private readonly JobsInspectionStore _store = new();
    private int _jobs;

    public void Dispose() => _db.Dispose();

    private async Task AddAsync(string jobKind, string status, DateTimeOffset updatedAt)
    {
        var key = $"job-{_jobs++}";
        await _db.Store.EnqueueOrGetAsync(key, jobKind);
        _db.Execute(
            "UPDATE jobs SET status = @status, updated_at = @updated WHERE dedupe_key = @key",
            ("@status", status), ("@updated", updatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture)), ("@key", key));
    }

    private async Task<FinishedJobs> CountAsync()
    {
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);
        return await _store.CountFinishedSinceAsync(uow, MidnightLocal);
    }

    [Fact]
    public async Task A_job_that_finished_today_counts_as_run_and_a_failed_one_also_counts_as_failed()
    {
        await AddAsync(RemuxPass, ProcessingJobStatus.Completed, MidnightLocal.AddHours(9));
        await AddAsync(RemuxPass, ProcessingJobStatus.Completed, MidnightLocal.AddHours(10));
        await AddAsync(RemuxPass, ProcessingJobStatus.Failed, MidnightLocal.AddHours(11));
        await AddAsync(RemuxPass, ProcessingJobStatus.HandlerOkFinalizeFailed, MidnightLocal.AddHours(12));

        Assert.Equal(new FinishedJobs(Run: 4, Failed: 2), await CountAsync());
    }

    [Fact]
    public async Task A_job_that_finished_before_the_day_began_is_not_counted()
    {
        await AddAsync(RemuxPass, ProcessingJobStatus.Completed, MidnightLocal.AddMinutes(-1));
        await AddAsync(RemuxPass, ProcessingJobStatus.Failed, MidnightLocal.AddDays(-1));

        Assert.Equal(new FinishedJobs(0, 0), await CountAsync());
    }

    [Fact]
    public async Task A_job_still_waiting_or_working_or_cancelled_is_not_counted()
    {
        await AddAsync(RemuxPass, ProcessingJobStatus.Pending, MidnightLocal.AddHours(9));
        await AddAsync(RemuxPass, ProcessingJobStatus.Leased, MidnightLocal.AddHours(9));
        await AddAsync(RemuxPass, ProcessingJobStatus.Cancelled, MidnightLocal.AddHours(9));

        Assert.Equal(new FinishedJobs(0, 0), await CountAsync());
    }

    [Fact]
    public async Task A_scan_that_found_nothing_wrong_is_not_counted_but_a_failed_scan_is()
    {
        await AddAsync(ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, ProcessingJobStatus.Completed, MidnightLocal.AddHours(9));
        await AddAsync(ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, ProcessingJobStatus.Completed, MidnightLocal.AddHours(10));
        await AddAsync(ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, ProcessingJobStatus.Failed, MidnightLocal.AddHours(11));

        Assert.Equal(new FinishedJobs(Run: 1, Failed: 1), await CountAsync());
    }
}
