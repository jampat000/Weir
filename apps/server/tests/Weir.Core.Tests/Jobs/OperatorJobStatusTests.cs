using Weir.Core.Jobs;
using Weir.Core.Media;
using Weir.Core.Processing;

namespace Weir.Core.Tests.Jobs;

/// <summary>Where a job's next step is somewhere in the app, it names the Activity page.</summary>
public sealed class OperatorJobStatusTests
{
    [Fact]
    public void A_cancelled_job_sends_the_person_to_Activity_to_start_the_file_again()
    {
        var status = OperatorJobStatus.Build("processing", "remux", "cancelled", lastError: null, payloadJson: null);

        Assert.Contains("start it again from Activity", status.NextAction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_still_being_written_sends_the_person_to_Activity_to_check_it_again()
    {
        var status = OperatorJobStatus.Build("processing", "remux", "failed", "file modified too recently", payloadJson: null);

        Assert.Contains("Check again from Activity", status.NextAction, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_job_points_at_its_error_and_at_Try_again_in_Activity()
    {
        var status = OperatorJobStatus.Build("processing", "remux", "failed", "ffmpeg exited with code 1", payloadJson: null);

        Assert.Equal("Read the error below, fix the cause, then use Try again in Activity.", status.NextAction);
    }

    [Theory]
    [InlineData(ToolFailureText.UnreadableFile)]
    [InlineData(ToolFailureText.NotReadableYet)]
    [InlineData(ToolFailureText.Generic)]
    [InlineData("ffprobe failed: broken")]
    public void A_job_that_failed_on_a_file_the_tools_could_not_read_says_so(string lastError)
    {
        var status = OperatorJobStatus.Build("processing", "remux", "failed", lastError, payloadJson: null);

        Assert.StartsWith("Weir could not read this media file", status.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CancelledFileReasons.InWeir)]
    [InlineData(CancelledFileReasons.ByManager)]
    public void A_cancelled_file_says_it_can_be_queued_again_from_Activity(string reason) =>
        Assert.Contains("queue it again from Activity", reason, StringComparison.Ordinal);
}
