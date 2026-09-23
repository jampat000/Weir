using Weir.Core.Processing;

namespace Weir.Core.Tests.Processing;

/// <summary>
/// #530: <c>passed_through</c> and <c>rejected</c> must be valid Processing file statuses in both responses
/// and filters. Leaving them out of the response and query schemas would 500 a file at either status and 422 a
/// filter naming one.
/// </summary>
public sealed class FileStatusesTests
{
    [Fact]
    public void The_eleven_original_statuses_keep_their_order_then_cancelled()
    {
        // #643: cancelled comes after the eleven original statuses, which keep their order.
        Assert.Equal(
            [
                "unprocessed", "processing", "processed", "processing_failed", "skipped", "disabled",
                "on_hold", "out_of_schedule", "blocked_upstream", "passed_through", "rejected", "cancelled",
            ],
            ProcessingFileStatuses.All);
    }

    [Theory]
    [InlineData(ProcessingFileStatuses.PassedThrough)]
    [InlineData(ProcessingFileStatuses.Rejected)]
    [InlineData(ProcessingFileStatuses.Cancelled)]
    public void Terminal_outcome_statuses_are_valid_status_values(string status) => Assert.Contains(status, ProcessingFileStatuses.All);

    [Fact]
    public void Withheld_statuses_do_not_include_the_two_terminal_ones()
    {
        // passed_through and rejected are terminal outcomes, not "Weir decided not to act yet" states.
        Assert.DoesNotContain(ProcessingFileStatuses.PassedThrough, ProcessingFileStatuses.Withheld);
        Assert.DoesNotContain(ProcessingFileStatuses.Rejected, ProcessingFileStatuses.Withheld);
    }
}
