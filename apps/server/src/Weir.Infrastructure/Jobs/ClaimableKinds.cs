using Weir.Core.Jobs;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// Which job kinds a claim may lease: kinds the workers have a handler for, plus kinds no worker may run,
/// which they claim only to refuse (see <see cref="JobHandlerRegistry"/>). A <see langword="null"/>
/// <see cref="ClaimableKinds"/> claims every kind.
/// </summary>
public sealed record ClaimableKinds(IReadOnlyList<string> HandledKinds, bool IncludeRefusedKinds)
{
    /// <summary>What the workers claim for a registry.</summary>
    public static ClaimableKinds For(JobHandlerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new ClaimableKinds(registry.JobKinds, IncludeRefusedKinds: true);
    }
}
