using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Rules;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassHandler
{
    private sealed record Claim(
        WireObject? Failure,
        ProcessingOperatorSettingsRecord? Operator = null,
        ProcessingLibraryRecord? Library = null,
        ProcessingRulesConfig? Rules = null,
        ProcessingPathRuntime? Runtime = null);

    /// <summary>
    /// Read what the pass needs and mark the file as being processed, then commit: no ffprobe or ffmpeg work runs while the
    /// worker holds a transaction.
    /// </summary>
    private Task<Claim> ClaimAsync(JobWorkContext context, string rel, string mediaScope, long? libraryId, CancellationToken cancellationToken) =>
        LockedWrites.RunAsync(
            _database,
            async uow =>
            {
                var operatorSettings = await _operatorSettings.EnsureAsync(uow).ConfigureAwait(false);
                var library = await ResolveLibraryAsync(uow, libraryId, mediaScope).ConfigureAwait(false);
                var rules = library is not null ? await RulesConfigForAsync(uow, library).ConfigureAwait(false) : null;
                rules ??= await LoadScopeRulesConfigAsync(uow, mediaScope).ConfigureAwait(false);
                ProcessingPathRuntime? runtime;
                string? problem;
                if (library is null)
                {
                    var label = mediaScope == "tv" ? "TV" : "Movies";
                    (runtime, problem) = (null, $"No library covers {label}. Add one on Processing → Libraries, then queue this work again.");
                }
                else
                {
                    (runtime, problem) = RemuxPassPaths.RuntimeForLibrary(library, _options.WeirHome);
                }

                if (problem is not null)
                {
                    return new Claim(new WireObject()
                        .Set("job_id", context.Id)
                        .Set("ok", false)
                        .Set("outcome", RemuxPassOutcomes.FailedBeforeExecution)
                        .Set("reason", problem)
                        .Set("relative_media_path", rel)
                        .Set("library_id", library?.Id ?? libraryId));
                }

                if (library is not null)
                {
                    await RemuxPassFileState.MarkFileStatusAsync(uow, library.Id, rel, ProcessingFileStatuses.Processing, "Weir has claimed this file and is checking it now.", _time.GetUtcNow())
                        .ConfigureAwait(false);
                }

                return new Claim(null, operatorSettings, library, rules, runtime);
            },
            _logger,
            "claim processing file",
            cancellationToken);

    /// <summary>The pass's library: by id when the payload carries one, else the seeded library for its scope.</summary>
    public static async Task<ProcessingLibraryRecord?> ResolveLibraryAsync(UnitOfWork uow, long? libraryId, string? mediaScope)
    {
        if (libraryId is { } id && await LibraryStore.GetAsync(uow, id).ConfigureAwait(false) is { } found)
        {
            return found;
        }

        return await LibraryStore.SeededForScopeAsync(uow, mediaScope ?? "movie").ConfigureAwait(false);
    }

    private static async Task<ProcessingRulesConfig?> RulesConfigForAsync(UnitOfWork uow, ProcessingLibraryRecord library) =>
        library.RuleSetId is { } ruleSetId && await LibraryStore.GetRuleSetAsync(uow, ruleSetId).ConfigureAwait(false) is { } ruleSet
            ? RemuxPassPaths.RulesConfigFor(ruleSet)
            : null;

    /// <summary>The seeded library's rule set for the scope, or the shipped defaults.</summary>
    private static async Task<ProcessingRulesConfig> LoadScopeRulesConfigAsync(UnitOfWork uow, string mediaScope)
    {
        var seeded = await LibraryStore.SeededForScopeAsync(uow, mediaScope).ConfigureAwait(false);
        var ruleSet = seeded?.RuleSetId is { } id ? await LibraryStore.GetRuleSetAsync(uow, id).ConfigureAwait(false) : null;
        return RuleSetConversion.ToRulesConfig(ruleSet);
    }
}
