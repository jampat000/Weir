using Weir.Core.Workers;

namespace Weir.Core.Readiness;

/// <summary><c>GET /health</c> body.</summary>
public sealed record HealthReport(string Status, IReadOnlyDictionary<string, string> Dependencies)
{
    public bool IsOk => Status == "ok";

    public static HealthReport FromDatabase(bool databaseConnected) => new(
        databaseConnected ? "ok" : "unhealthy",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["database"] = databaseConnected ? "ok" : "failed" });
}

/// <summary>One startup area in the readiness report.</summary>
public sealed record ReadinessStep(string Name, string Status, string Detail);

/// <summary><c>GET /api/v1/system/readiness</c> body.</summary>
public sealed record ReadinessReport(
    bool Ready,
    string Version,
    string Status,
    double StartupSeconds,
    IReadOnlyList<ReadinessStep> Steps,
    IReadOnlyList<WorkerLaneHealth> WorkerHealth);

/// <summary>What readiness is computed from, gathered by the caller.</summary>
/// <param name="StartupElapsed">Time since startup began.</param>
/// <param name="DatabaseReady">The database was opened at startup and answers a probe now.</param>
/// <param name="StartupComplete">Startup finished and shutdown has not begun.</param>
/// <param name="WorkerHealth">Worker lanes, or <see langword="null"/> when settings were never loaded.</param>
/// <param name="WatcherSummary">The filesystem watcher's <c>(ok, sentence)</c>.</param>
/// <param name="RecoveryComplete">
/// Startup recovery (#718) has finished, or nothing gates it. It no longer holds up Kestrel from listening, so
/// while it is still running the worker lanes correctly read as "starting" rather than "failed".
/// </param>
public sealed record ReadinessInputs(
    TimeSpan StartupElapsed,
    bool DatabaseReady,
    bool StartupComplete,
    IReadOnlyList<WorkerLaneHealth>? WorkerHealth,
    (bool Ok, string Detail) WatcherSummary,
    bool RecoveryComplete = true);

/// <summary>Builds the readiness report from the database, startup, worker and watcher state.</summary>
public static class ReadinessBuilder
{
    /// <summary>What the watcher reports when no library is being watched.</summary>
    public static readonly (bool Ok, string Detail) NoWatchedLibraries =
        (true, "No libraries are being watched for filesystem events.");

    public static ReadinessReport Build(ReadinessInputs inputs, string version)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var startupComplete = inputs.StartupComplete;
        var workerHealth = inputs.WorkerHealth ?? [];
        var workersReady = inputs.WorkerHealth is null
            ? startupComplete
            : startupComplete && inputs.RecoveryComplete && workerHealth.All(row => row.Status is "healthy" or "disabled");
        // Recovery runs after Kestrel is already listening (#718), so a worker lane waiting on it reads as
        // still starting, not failed, even once the rest of startup has finished.
        var workersStillStarting = !startupComplete || !inputs.RecoveryComplete;

        List<ReadinessStep> steps =
        [
            new(
                "database",
                inputs.DatabaseReady ? "ready" : startupComplete ? "failed" : "starting",
                inputs.DatabaseReady
                    ? "Local database is connected and migrations are complete."
                    : startupComplete
                        ? "Weir could not verify the local database connection."
                        : "Weir is preparing the local database."),
            new(
                "workers",
                workersReady ? "ready" : workersStillStarting ? "starting" : "failed",
                workersReady
                    ? "Background workers and schedules are ready."
                    : workersStillStarting
                        ? "Weir is starting background workers and schedules."
                        : "One or more background workers are stale or stopped."),
            // Falling back to the periodic scan is slower, not broken, so this reports rather than fails.
            new("filesystem_watcher", inputs.WatcherSummary.Ok ? "ready" : "failed", inputs.WatcherSummary.Detail),
        ];

        var ready = steps.All(step => step.Status == "ready");
        var status = ready ? "ready" : steps.Any(step => step.Status == "failed") ? "failed" : "starting";
        return new ReadinessReport(
            ready,
            version,
            status,
            Math.Round(Math.Max(0.0, inputs.StartupElapsed.TotalSeconds), 3, MidpointRounding.ToEven),
            steps,
            workerHealth);
    }
}
