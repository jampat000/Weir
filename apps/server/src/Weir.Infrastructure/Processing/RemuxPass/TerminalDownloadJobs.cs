using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// The finished <c>jobs</c> rows of the three per-file download job kinds (remux pass, pass-through, reject —
/// the same set <see cref="HandoffOriginCarry"/> reads) that name one file: what the failed-jobs alert and the
/// overview counters on Processing still read by status, whether or not the file's own <c>files</c> row exists
/// (#780). Forgetting a file's History deletes these too, so a file gone from History cannot still be reported
/// there as finished or failed work.
/// </summary>
public static class TerminalDownloadJobs
{
    private static readonly string[] Kinds = [RemuxPassOutcomes.JobKind, IntakeRules.PassThroughJobKind, IntakeRules.RejectJobKind];

    /// <summary>Deletes every terminal job of these kinds naming this file. Returns how many rows were removed.</summary>
    public static async Task<int> DeleteForFileAsync(UnitOfWork uow, long? libraryId, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var path = WireStrings.Strip(relativePath);
        var (whereSql, parameters) = TerminalJobsOfKindsClause();
        var rows = await uow.QueryAsync(
            $"SELECT id, payload_json FROM jobs WHERE {whereSql}",
            reader => (Id: reader.GetInt64(0), Payload: reader.IsDBNull(1) ? null : reader.GetString(1)),
            parameters).ConfigureAwait(false);

        var matching = rows.Where(row => NamesFile(row.Payload, path, libraryId)).Select(row => row.Id).ToList();
        if (matching.Count == 0)
        {
            return 0;
        }

        var idNames = matching.Select((_, index) => $"@id{index}").ToArray();
        var idParameters = matching.Select((id, index) => ($"@id{index}", (object?)id)).ToArray();
        return await uow.ExecuteAsync($"DELETE FROM jobs WHERE id IN ({string.Join(", ", idNames)})", idParameters).ConfigureAwait(false);
    }

    private static (string Sql, (string Name, object? Value)[] Parameters) TerminalJobsOfKindsClause()
    {
        var kindNames = Kinds.Select((_, index) => $"@kind{index}").ToArray();
        var statusNames = ProcessingJobStatus.Terminal.Select((_, index) => $"@status{index}").ToArray();
        var parameters = Kinds.Select((kind, index) => ($"@kind{index}", (object?)kind))
            .Concat(ProcessingJobStatus.Terminal.Select((status, index) => ($"@status{index}", (object?)status)))
            .ToArray();
        return ($"job_kind IN ({string.Join(", ", kindNames)}) AND status IN ({string.Join(", ", statusNames)})", parameters);
    }

    /// <summary>Whether a job payload names this file: same relative path, and same library when one is given.</summary>
    private static bool NamesFile(string? payloadJson, string relativePath, long? libraryId)
    {
        if (string.IsNullOrEmpty(payloadJson))
        {
            return false;
        }

        WireValue parsed;
        try
        {
            parsed = WireJsonParser.Parse(payloadJson);
        }
        catch (WireJsonDecodeException)
        {
            return false;
        }

        if (parsed is not WireObject dict || dict.Get("relative_media_path") is not WireString path || WireStrings.Strip(path.Value) != relativePath)
        {
            return false;
        }

        return libraryId is not { } wanted || dict.Get("library_id") is not WireInteger jobLibrary || jobLibrary.Value == wanted;
    }
}
