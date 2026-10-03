using Weir.Core.LibraryMode;
using Weir.Core.Processing;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Scheduling;

namespace Weir.Infrastructure.Tests.Scheduling;

/// <summary>Which listed task a queued job's run counts towards.</summary>
public sealed class ScheduledJobTasksTests
{
    [Fact]
    public void A_watched_folder_scan_counts_towards_its_workflows_scan()
    {
        var key = ScheduledJobTasks.KeyFor(ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, "{\"library_id\": 7, \"media_scope\": \"movie\"}");

        Assert.Equal(ScheduledJobTasks.ScanKey(7), key);
    }

    [Theory]
    [InlineData(LibraryModeJobKinds.ScanKind)]
    [InlineData(LibraryModeJobKinds.CleanKind)]
    public void A_library_scan_or_clean_counts_towards_its_workflows_library_clean(string jobKind)
    {
        Assert.Equal(ScheduledJobTasks.LibraryCleanKey(3), ScheduledJobTasks.KeyFor(jobKind, "{\"library_id\":3}"));
    }

    [Fact]
    public void A_cleanup_job_counts_towards_its_family_whatever_its_payload()
    {
        Assert.Equal(ScheduledJobTasks.LeftoverFiles.Key, ScheduledJobTasks.KeyFor(PeriodicJobKinds.WorkTempStaleSweep, "{\"media_scope\":\"tv\"}"));
        Assert.Equal(ScheduledJobTasks.UnclaimedCopies.Key, ScheduledJobTasks.KeyFor(PeriodicJobKinds.UnclaimedHandbackCleanup, null));
    }

    [Theory]
    [InlineData("processing.file.remux_pass.v1", "{\"library_id\":1}")]
    [InlineData(ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, "{\"media_scope\":\"movie\"}")]
    [InlineData(ProcessingWatchedFolderScanDispatchJobKinds.ScanDispatch, "{\"library_id\":true}")]
    [InlineData(LibraryModeJobKinds.CleanKind, null)]
    public void A_job_no_timer_is_responsible_for_counts_towards_nothing(string jobKind, string? payload)
    {
        Assert.Null(ScheduledJobTasks.KeyFor(jobKind, payload));
    }

    [Fact]
    public void A_workflows_tasks_are_labelled_with_its_name()
    {
        Assert.Equal(("Scan Movies", "Clean Movies library"), (ScheduledJobTasks.ScanLabel("Movies"), ScheduledJobTasks.LibraryCleanLabel("Movies")));
    }
}
