using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// A file the rules reject with no media manager involved: the reject policy is off, and the file did not come from a
/// hand-off. Nobody else is told, so the file is recorded as rejected on its own and a person decides what happens to it
/// from Activity (#817). A rejection a manager is asked to act on keeps its own wording and route.
/// </summary>
public static class WeirOnlyRejection
{
    /// <summary>What follows the reason when the library leaves rejected files alone.</summary>
    public const string LeftInPlace = "The file was left where it is.";

    /// <summary>What follows the reason while the library's delete-rejected-files choice is still to run.</summary>
    public const string DeleteQueued = "Weir will now delete only this file.";

    /// <summary>What follows the reason once that choice has run.</summary>
    public const string Deleted = "The file was deleted, because you chose to delete rejected files.";

    /// <summary>Whether a rejection in <paramref name="library"/> of a file from <paramref name="origin"/> involves no manager.</summary>
    public static bool Applies(ProcessingLibraryRecord library, WireObject? origin)
    {
        ArgumentNullException.ThrowIfNull(library);
        return origin is null && ProcessingFailurePolicies.Normalize(library.FailurePolicy) != ProcessingFailurePolicies.Reject;
    }

    /// <summary>
    /// Marks the result as a rejection no manager is involved in, and puts it in the words a person reads: the plain
    /// explanation the pass wrote, when it wrote one, replaces the technical sentence a manager's report would carry.
    /// </summary>
    public static void Present(WireObject result)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Set(RejectionResultKeys.WithoutManager, true);
        if (result.Get("rejection_explanation") is WireString { Value.Length: > 0 } explanation)
        {
            result.Set("reason", explanation.Value);
            result.Set("preflight_reason", explanation.Value);
        }

        if (result.Get(RejectionResultKeys.Summary) is not { IsTruthy: true })
        {
            switch (result.Get("rejection_kind"))
            {
                case WireString { Value: "no_video_stream" }:
                    result.Set(RejectionResultKeys.Summary, "it has no video");
                    break;
                case WireString { Value: RejectionKinds.UnreadableFile }:
                    result.Set(RejectionResultKeys.Summary, "Weir couldn't read it");
                    break;
            }
        }
    }
}
