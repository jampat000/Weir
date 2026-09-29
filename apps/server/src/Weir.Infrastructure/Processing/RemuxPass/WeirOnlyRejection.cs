using Weir.Core.Json;
using Weir.Core.Processing;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// A file the rules reject with no media manager involved: the reject policy is off, and the file did not come from a
/// hand-off. Nobody else is told, so the file is recorded as rejected on its own and a person decides what happens to it
/// from History (#817). A rejection a manager is asked to act on keeps its own wording and route.
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
    /// Puts the reason in the words a person reads: the plain explanation the pass wrote, when it wrote one, replaces the
    /// technical sentence a manager's report would carry.
    /// </summary>
    public static void UsePlainReason(WireObject result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Get("rejection_explanation") is WireString { Value.Length: > 0 } explanation)
        {
            result.Set("reason", explanation.Value);
            result.Set("preflight_reason", explanation.Value);
        }
    }
}
