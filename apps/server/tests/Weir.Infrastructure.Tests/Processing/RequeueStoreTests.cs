using Weir.Infrastructure.Processing;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>The sentence a bulk "try again" reports, counted in English in the singular and the plural.</summary>
public sealed class RequeueStoreTests
{
    [Theory]
    [InlineData(1, 0, "Queued 1 file again. It starts as capacity frees up.")]
    [InlineData(3, 0, "Queued 3 files again. They start as capacity frees up.")]
    [InlineData(1, 1, "Queued 1 file again. 1 could not be queued because its workflow or original is gone.")]
    [InlineData(2, 2, "Queued 2 files again. 2 could not be queued because their workflow or original is gone.")]
    [InlineData(0, 1, "Nothing was queued: 1 file has lost its workflow or original.")]
    [InlineData(0, 2, "Nothing was queued: 2 files have lost their workflow or original.")]
    [InlineData(0, 0, "Nothing matched, so nothing was queued.")]
    public void A_bulk_requeue_reports_its_counts_in_english(int requeued, int skipped, string expected) =>
        Assert.Equal(expected, RequeueStore.BulkDetail(requeued, skipped));

    [Theory]
    [InlineData(0, 0, 1, "Nothing was queued: 1 file was already cleaned, so Weir left it alone.")]
    [InlineData(0, 0, 3, "Nothing was queued: 3 files were already cleaned, so Weir left them alone.")]
    [InlineData(2, 0, 1, "Queued 2 files again. They start as capacity frees up. 1 file was already cleaned, so Weir left it alone.")]
    [InlineData(1, 1, 2, "Queued 1 file again. 1 could not be queued because its workflow or original is gone. 2 files were already cleaned, so Weir left them alone.")]
    public void A_bulk_requeue_says_how_many_sources_were_already_cleaned(int requeued, int lost, int alreadyCleaned, string expected) =>
        Assert.Equal(expected, RequeueStore.BulkDetail(requeued, lost, alreadyCleaned));
}
