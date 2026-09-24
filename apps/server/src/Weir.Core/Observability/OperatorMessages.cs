using Weir.Core.Json;

namespace Weir.Core.Observability;

/// <summary>Plain-language operator labels and the Activity detail envelope.</summary>
public static class OperatorMessages
{
    private static readonly Dictionary<string, string> ProviderLabels = new(StringComparer.Ordinal)
    {
        ["deluno"] = "Deluno", ["radarr"] = "Radarr", ["sonarr"] = "Sonarr", ["tmdb"] = "TMDb",
    };

    private static readonly Dictionary<string, string> ScopeLabels = new(StringComparer.Ordinal)
    {
        ["movie"] = "Movies", ["movies"] = "Movies", ["tv"] = "TV episodes", ["episode"] = "TV episodes",
    };

    public static string? ProviderLabel(string? provider)
    {
        if (string.IsNullOrEmpty(provider))
        {
            return null;
        }

        var value = provider.Trim();
        return ProviderLabels.TryGetValue(value.ToLowerInvariant(), out var label) ? label : value;
    }

    public static string? MediaScopeLabel(string? mediaScope)
    {
        if (string.IsNullOrEmpty(mediaScope))
        {
            return null;
        }

        var value = mediaScope.Trim();
        return ScopeLabels.TryGetValue(value.ToLowerInvariant(), out var label) ? label : value;
    }

    /// <summary>Numeric counts only (nulls dropped), never negative.</summary>
    public static WireObject CountSummary(IReadOnlyList<KeyValuePair<string, long?>> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var summary = new WireObject();
        foreach (var (key, value) in counts)
        {
            if (value is { } v)
            {
                summary.Set(key, Math.Max(0, v));
            }
        }

        return summary;
    }

    /// <summary>The structured detail an Activity event carries: who, what, result, severity, and the optional labels, counts and messages.</summary>
    public static WireObject ActivityDetailEnvelope(
        string module,
        string action,
        string trigger,
        string result,
        string? provider = null,
        string? mediaScope = null,
        IReadOnlyList<KeyValuePair<string, long?>>? counts = null,
        string? userMessage = null,
        string? nextAction = null)
    {
        var payload = new WireObject()
            .Set("module", module)
            .Set("action", action)
            .Set("trigger", trigger)
            .Set("result", result)
            .Set("severity", Diagnostics.SeverityForResult(result));
        if (ProviderLabel(provider) is { } providerLabel)
        {
            payload.Set("provider", providerLabel);
        }

        if (MediaScopeLabel(mediaScope) is { } scopeLabel)
        {
            payload.Set("media_scope_label", scopeLabel);
        }

        if (!string.IsNullOrEmpty(mediaScope))
        {
            payload.Set("media_scope", mediaScope);
        }

        if (counts is { Count: > 0 })
        {
            payload.Set("counts", CountSummary(counts));
        }

        if (!string.IsNullOrEmpty(userMessage))
        {
            payload.Set("user_message", userMessage);
        }

        if (!string.IsNullOrEmpty(nextAction))
        {
            payload.Set("next_action", nextAction);
        }

        return payload;
    }
}
