using Microsoft.Extensions.Logging;
using Weir.Core.Logs;

namespace Weir.Infrastructure.Tests.SystemLog;

public sealed partial class SystemLogReaderTests
{
    [Fact]
    public async Task Each_workflow_is_counted_with_every_other_filter_applied_but_not_its_own()
    {
        await AddEvent(minutesAgo: 5, "library.scan_completed", "Movies scanned", result: "success", libraryId: 1);
        await AddEvent(minutesAgo: 6, "library.scan_completed", "Shows scanned", result: "failed", libraryId: 2);
        await AddEvent(minutesAgo: 7, "auth.login_succeeded", "Signed in", result: "success");
        await AddJob(minutesAgo: 8, "processing.library.scan.v1", "completed", payload: "{\"library_id\": 1}");
        AddServerLine(minutesAgo: 9, LogLevel.Warning, "weir.library_mode.router", "A library is slow");

        Assert.Equal(new Dictionary<long, long> { [1] = 2, [2] = 1 }, (await Read(new SystemLogFilter())).Counts.ByWorkflow);
        Assert.Equal(new Dictionary<long, long> { [1] = 2, [2] = 1 }, (await Read(new SystemLogFilter { WorkflowId = 1 })).Counts.ByWorkflow);
        Assert.Equal(new Dictionary<long, long> { [1] = 1 }, (await Read(new SystemLogFilter { Sources = [SystemLogSource.Job] })).Counts.ByWorkflow);
        Assert.Equal(new Dictionary<long, long> { [2] = 1 }, (await Read(new SystemLogFilter { Levels = [SystemLogLevels.Error] })).Counts.ByWorkflow);
        Assert.Empty((await Read(new SystemLogFilter { Sources = [SystemLogSource.Server] })).Counts.ByWorkflow);
    }
}
