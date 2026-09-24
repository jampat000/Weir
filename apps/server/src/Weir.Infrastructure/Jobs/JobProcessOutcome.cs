namespace Weir.Infrastructure.Jobs;

/// <summary>What one worker pass came to: nothing to claim, or one job handled.</summary>
public enum JobProcessOutcome
{
    Idle,
    Processed,
}
