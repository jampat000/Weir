using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Rules;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassRunner
{
    /// <summary>
    /// #537 item 4: the metadata lookup decides which audio the planner prefers. Declining (no provider, no match, unreachable)
    /// leaves the language preferences in charge, with a note saying so.
    /// </summary>
    private async Task<(ProcessingRulesConfig Config, PyDict Record)> ApplyOriginalLanguageAsync(
        ProcessingRulesConfig config,
        OriginalLanguageRules rules,
        string scope,
        string relativeMediaPath,
        HandoffOrigin? origin,
        IReadOnlyList<ProbeStreamInfo> audio,
        CancellationToken cancellationToken)
    {
        LookupResult lookup;
        try
        {
            lookup = await _originalLanguage.LookupAsync(scope, relativeMediaPath, origin, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed lookup degrades to the language preferences; the pass still runs.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(exception, "Original-language lookup failed for {Path}.", relativeMediaPath);
            lookup = new LookupResult { Status = LookupResult.StatusUnreachable, Detail = $"The metadata lookup failed ({exception.Message})." };
        }

        var tracks = audio
            .Where(stream => stream.Index is not null)
            .Select(stream => new OriginalLanguageTrack((int)stream.Index!.Value, stream.Tag("language") ?? string.Empty))
            .ToList();
        var outcome = OriginalLanguage.SelectTracks(rules, lookup, tracks);
        var record = new PyDict()
            .Set("lookup_status", lookup.Status)
            .Set("lookup_detail", lookup.Detail)
            .Set("original_language", lookup.Metadata?.OriginalLanguage)
            .Set("preferred_audio_indices", new PyList(outcome.PreferredIndices.Select(index => (PyJson)new PyInt(index))))
            .Set("note", outcome.Note);
        return (config.WithOriginalLanguage(outcome), record);
    }
}
