using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing;
using Weir.Core.Rules;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>One pass's inputs.</summary>
public sealed record RemuxPassRequest
{
    public required ProcessingPathRuntime Runtime { get; init; }
    public required string RelativeMediaPath { get; init; }

    /// <summary>Issue #545 item 5: which library this pass belongs to, so its file-row writes never touch another library's row.</summary>
    public long? LibraryId { get; init; }

    public ProcessingRulesConfig? RulesConfig { get; init; }
    public long? MinFileAgeSeconds { get; init; }
    public string? MediaScope { get; init; } = "movie";
    public long? CurrentJobId { get; init; }
    public Action<WireObject>? ProgressReporter { get; init; }
    public long MinInputFileSizeMb { get; init; }
    public long MinimumFreeDiskSpaceMb { get; init; }
    public bool PassThroughUnchanged { get; init; }

    /// <summary>Settings › Performance "Keep the half-written copy": a failed write stays in the work folder.</summary>
    public bool KeepFailedWorkFiles { get; init; }

    /// <summary>The hand-off this file came from, when it did: its release name feeds the original-language lookup.</summary>
    public HandoffOrigin? Origin { get; init; }

    /// <summary>
    /// An operator's hand-picked track choice (issue #501). When set, the pass re-probes, checks the source fingerprint
    /// and every kept index against <see cref="ManualPlanFingerprint"/>, and builds the plan straight from the choice
    /// instead of calling <see cref="RemuxRules.PlanRemux"/>.
    /// </summary>
    public ManualPlanChoice? ManualPlan { get; init; }

    /// <summary>The source fingerprint recorded when the operator chose the tracks in <see cref="ManualPlan"/>.</summary>
    public SourceFingerprint? ManualPlanFingerprint { get; init; }
}
