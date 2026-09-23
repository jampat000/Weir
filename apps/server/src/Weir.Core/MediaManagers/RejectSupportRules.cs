namespace Weir.Core.MediaManagers;

/// <summary>
/// The pure constants of the opt-in <c>reject</c> failure policy. The evaluation itself needs live manager ports,
/// so it lives in Weir.Infrastructure (<c>RejectSupportEvaluator</c>, <c>ProcessingRejectHandler</c>).
/// </summary>
public static class RejectSupportRules
{
    /// <summary>What a manager's manifest must advertise before Weir will reject through a hand-off.</summary>
    public const string RejectCapability = "processor-reject-regrab";

    /// <summary>Minimum spacing between outbound rejects, across the whole process.</summary>
    public static readonly TimeSpan MinSecondsBetweenRejects = TimeSpan.FromSeconds(2);
}

/// <summary>Whether a library's managers can take a rejection, and the sentence that explains it.</summary>
public sealed record RejectSupportResult(bool Available, string Reason);
