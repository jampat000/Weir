namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>The environment-level settings a pass reads (<c>WeirSettings</c> fields).</summary>
public sealed record RemuxPassSettings
{
    public int ProbeSizeMb { get; init; } = 10;
    public int AnalyzeDurationSeconds { get; init; } = 10;
    public int WatchedFolderMinFileAgeSeconds { get; init; } = 60;
    public int MovieOutputCleanupMinAgeSeconds { get; init; }
    public int TvOutputCleanupMinAgeSeconds { get; init; }
}
