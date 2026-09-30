using Weir.Core.Json;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassHandler
{
    /// <summary>
    /// Minutes between looks at a file waiting for room on a drive, the last one repeating. A full drive can stay full for
    /// days, so the looks spread out rather than fill Activity with one row every few minutes.
    /// </summary>
    public static readonly IReadOnlyList<int> DiskSpaceWaitMinutes = [10, 30, 60];

    /// <summary>
    /// A file that could not start because a drive is short of the space to keep free is looked at again later and stays
    /// waiting until then, never recorded as done. Booked before the file's state is written, so its hold ends when the look is due.
    /// </summary>
    private async Task DeferUntilDiskSpaceAsync(long jobId, WireObject data, WireObject? origin, WireObject result, CancellationToken cancellationToken)
    {
        if (_jobs is null || result.Get("not_ready_kind") is not WireString { Value: RemuxPassRunner.DiskSpaceWait })
        {
            return;
        }

        var looks = data.Get("disk_space_looks") is WireInteger counted ? (long)counted.Value : 0;
        var minutes = DiskSpaceWaitMinutes[(int)Math.Min(looks, DiskSpaceWaitMinutes.Count - 1)];
        await BookAnotherLookAsync(
            data.Copy().Set("disk_space_looks", looks + 1),
            origin,
            $"{RemuxPassOutcomes.JobKind}:disk-space-wait:{jobId}",
            _time.GetUtcNow().AddMinutes(minutes),
            result,
            cancellationToken).ConfigureAwait(false);
    }
}
