using Microsoft.Data.Sqlite;
using Weir.Core.Jobs;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Infrastructure.Jobs;

/// <summary>
/// What a job costs against the resolution budget: every job that reads a whole video, whichever way it arrived, is costed by
/// that video's resolution. A file whose resolution is not known yet costs what a 1080p file does
/// (<see cref="RunnerBudget.UnknownResolutionCostsLike"/>), and a running job is corrected once its resolution is measured.
/// </summary>
internal static class RunnerCosts
{
    /// <summary>The budget in force, read with the connection and transaction the caller already holds.</summary>
    public static RunnerBudget ReadBudget(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT runner_capacity, runner_cost_sd, runner_cost_720p, runner_cost_1080p, runner_cost_4k FROM operator_settings WHERE id = 1";
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? RunnerBudget.FromSettings(
                WorkAdmissionReader.Long(reader.GetValue(0)), WorkAdmissionReader.Long(reader.GetValue(1)), WorkAdmissionReader.Long(reader.GetValue(2)),
                WorkAdmissionReader.Long(reader.GetValue(3)), WorkAdmissionReader.Long(reader.GetValue(4)))
            : RunnerBudget.Default;
    }

    /// <summary>
    /// The cost of a job about to be queued: for a file pass, that of the resolution recorded for its file by an earlier pass or
    /// scan; for a file that has none yet, that of an unknown resolution. Any other kind of job costs nothing.
    /// </summary>
    public static int ForJob(SqliteConnection connection, SqliteTransaction transaction, string jobKind, string? payloadJson)
    {
        if (!string.Equals(jobKind, RemuxPassOutcomes.JobKind, StringComparison.Ordinal))
        {
            return 0;
        }

        var payload = JobPayload.ParseObject(payloadJson);
        var (width, height) = RecordedDimensions(
            connection, transaction, JobPayload.StrictInteger(payload, "library_id"), JobPayload.StringProperty(payload, "relative_media_path"));
        return ReadBudget(connection, transaction).CostFor(RunnerUnits.ResolutionClassForDimensions(width, height));
    }

    /// <summary>The cost of a library file's clean, from the probe the scan kept for it.</summary>
    public static int ForProbeJson(SqliteConnection connection, SqliteTransaction transaction, string? probeJson) =>
        ReadBudget(connection, transaction).CostFor(RunnerUnits.ResolutionClassForProbeJson(probeJson));

    /// <summary>Puts the measured resolution's cost on a job that is running, so what starts after it counts it.</summary>
    public static void RecordMeasured(SqliteConnection connection, SqliteTransaction transaction, long jobId, string resolutionClass)
    {
        var cost = ReadBudget(connection, transaction).CostFor(resolutionClass);
        ProcessingJobStore.Execute(
            connection,
            transaction,
            "UPDATE jobs SET runner_cost = @cost WHERE id = @id AND status = @leased AND runner_cost <> @cost",
            ("@cost", cost),
            ("@id", jobId),
            ("@leased", ProcessingJobStatus.Leased));
    }

    private static (long? Width, long? Height) RecordedDimensions(SqliteConnection connection, SqliteTransaction transaction, long? libraryId, string? relativePath)
    {
        if (libraryId is null || string.IsNullOrEmpty(relativePath))
        {
            return (null, null);
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT video_width, video_height FROM files WHERE library_id = @library AND relative_path = @path";
        command.Parameters.AddWithValue("@library", libraryId.Value);
        command.Parameters.AddWithValue("@path", relativePath);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.IsDBNull(0) ? null : reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1))
            : (null, null);
    }
}
