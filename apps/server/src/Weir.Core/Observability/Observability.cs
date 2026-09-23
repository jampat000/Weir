using System.Text.RegularExpressions;
using Weir.Core.Json;

namespace Weir.Core.Observability;

/// <summary>Shared diagnostics vocabulary: secret redaction and result severities.</summary>
public static partial class Diagnostics
{
    public static readonly IReadOnlyList<string> SecretFieldFragments = ["api_key", "apikey", "authorization", "cookie", "password", "secret", "token"];

    /// <summary>Redacts a text value: all of it when the key looks secret, otherwise any <c>key=value</c> secret assignment inside it.</summary>
    public static string SanitizeText(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (SecretFieldFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return "[redacted]";
        }

        return SecretAssignment().Replace(value, match => match.Groups[1].Value + "[redacted]");
    }

    /// <summary>The severity for a result: failed is an error, warning or retrying a warning, anything else info.</summary>
    public static string SeverityForResult(string result) => (result ?? string.Empty).ToLowerInvariant() switch
    {
        "failed" => "error",
        "warning" or "retrying" => "warning",
        _ => "info",
    };

    [GeneratedRegex(@"(\b(?:api[_-]?key|authorization|cookie|password|secret|token)\b\s*[:=]\s*)[^,\s;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignment();
}

/// <summary>One diagnostics event in the shared vocabulary.</summary>
public sealed record DiagnosticEvent(
    string Module,
    string Action,
    string Trigger,
    string Result,
    string Severity,
    string? Provider = null,
    string? MediaScope = null,
    string? CorrelationId = null,
    string? Reason = null,
    string? NextAction = null,
    IReadOnlyList<KeyValuePair<string, long>>? Counts = null)
{
    /// <summary>The event as a payload: optional fields only when set, secret-looking values redacted.</summary>
    public PyDict AsSafeDict()
    {
        var payload = new PyDict()
            .Set("module", Module)
            .Set("action", Action)
            .Set("trigger", Trigger)
            .Set("result", Result)
            .Set("severity", Severity);
        foreach (var (key, value) in new[]
        {
            ("provider", Provider), ("media_scope", MediaScope), ("correlation_id", CorrelationId),
            ("reason", Reason), ("next_action", NextAction),
        })
        {
            if (!string.IsNullOrEmpty(value))
            {
                payload.Set(key, Diagnostics.SanitizeText(key, value));
            }
        }

        if (Counts is { Count: > 0 })
        {
            var counts = new PyDict();
            foreach (var (key, value) in Counts)
            {
                counts.Set(key, value);
            }

            payload.Set("counts", counts);
        }

        return payload;
    }
}

/// <summary>Plain-language operator labels and the Activity detail envelope.</summary>
public static class OperatorMessages
{
    private static readonly Dictionary<string, string> ProviderLabels = new(StringComparer.Ordinal)
    {
        ["emby"] = "Emby", ["jellyfin"] = "Jellyfin", ["plex"] = "Plex", ["radarr"] = "Radarr", ["sonarr"] = "Sonarr",
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
    public static PyDict CountSummary(IReadOnlyList<KeyValuePair<string, long?>> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var summary = new PyDict();
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
    public static PyDict ActivityDetailEnvelope(
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
        var payload = new PyDict()
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

/// <summary>The failure kinds operators see.</summary>
public enum FailureKind
{
    Auth,
    Credential,
    Network,
    Filesystem,
    RateLimit,
    Validation,
    NotFound,
    Internal,
}

/// <summary>A failure as the classifier sees it: a stable type name, the message and its family.</summary>
/// <param name="TypeName">
/// The stable failure type name (for example <c>PermissionError</c>), written into <c>technical_detail</c> and
/// searched by the classifier; the names stay fixed so stored details and classification stay consistent.
/// </param>
/// <param name="Message">The failure message.</param>
/// <param name="Category">Which failure family it belongs to.</param>
public sealed record FailureSubject(string TypeName, string Message, ExceptionCategory Category);

/// <summary>The failure families <see cref="FailureMessages.Classify"/> distinguishes.</summary>
public enum ExceptionCategory
{
    Other,

    /// <summary>A missing, clashing, wrong-kind or forbidden file or folder.</summary>
    Filesystem,

    /// <summary>A connection failure, a timeout, or any other I/O error.</summary>
    NetworkOrOs,

    /// <summary>An invalid value or type.</summary>
    Validation,
}

/// <summary>An operator-facing failure: what failed, why, and what happens next.</summary>
public sealed record OperatorFailure(
    string Module,
    string Action,
    FailureKind Kind,
    bool Recoverable,
    string Message,
    string Why,
    string WhatHappensNext,
    string? NextAction,
    string? TechnicalDetail)
{
    public PyDict AsDict()
    {
        var output = new PyDict()
            .Set("failure_kind", FailureMessages.KindText(Kind))
            .Set("recoverable", Recoverable)
            .Set("what_failed", $"{Module} {Action}")
            .Set("why", Why)
            .Set("what_happens_next", WhatHappensNext)
            .Set("user_message", Message);
        if (!string.IsNullOrEmpty(NextAction))
        {
            output.Set("next_action", NextAction);
        }

        if (!string.IsNullOrEmpty(TechnicalDetail))
        {
            output.Set("technical_detail", TechnicalDetail);
        }

        return output;
    }
}

/// <summary>Classifies failures and words them for operators.</summary>
public static class FailureMessages
{
    public static string KindText(FailureKind kind) => kind switch
    {
        FailureKind.Auth => "auth",
        FailureKind.Credential => "credential",
        FailureKind.Network => "network",
        FailureKind.Filesystem => "filesystem",
        FailureKind.RateLimit => "rate_limit",
        FailureKind.Validation => "validation",
        FailureKind.NotFound => "not_found",
        _ => "internal",
    };

    /// <summary>The failure kind: keywords in <c>"{type}: {message}"</c> first, then the failure family.</summary>
    public static FailureKind Classify(FailureSubject exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var text = $"{exception.TypeName}: {exception.Message}".ToLowerInvariant();
        if (text.Contains("rate limit", StringComparison.Ordinal) || text.Contains("ratelimit", StringComparison.Ordinal) ||
            text.Contains("rate-limit", StringComparison.Ordinal) || text.Contains("429", StringComparison.Ordinal))
        {
            return FailureKind.RateLimit;
        }

        if (text.Contains("credential", StringComparison.Ordinal) || text.Contains("api key", StringComparison.Ordinal) ||
            text.Contains("token", StringComparison.Ordinal) || text.Contains("secret", StringComparison.Ordinal))
        {
            return FailureKind.Credential;
        }

        if (text.Contains("unauthorized", StringComparison.Ordinal) || text.Contains("forbidden", StringComparison.Ordinal) ||
            text.Contains("401", StringComparison.Ordinal) || text.Contains("403", StringComparison.Ordinal))
        {
            return FailureKind.Auth;
        }

        if (exception.Category == ExceptionCategory.Filesystem)
        {
            return FailureKind.Filesystem;
        }

        if (text.Contains("not found", StringComparison.Ordinal) || text.Contains("404", StringComparison.Ordinal))
        {
            return FailureKind.NotFound;
        }

        return exception.Category switch
        {
            ExceptionCategory.NetworkOrOs => FailureKind.Network,
            ExceptionCategory.Validation => FailureKind.Validation,
            _ => FailureKind.Internal,
        };
    }

    public static string WhyForKind(FailureKind kind, string? provider)
    {
        var where = string.IsNullOrEmpty(provider) ? string.Empty : $" from {OperatorMessages.ProviderLabel(provider)}";
        return kind switch
        {
            FailureKind.RateLimit => $"The provider{where} temporarily limited requests.",
            FailureKind.Credential => $"Weir could not use the saved credentials{where}.",
            FailureKind.Auth => $"The service{where} rejected the credentials or permission level.",
            FailureKind.Network => $"Weir could not reach the service{where} over the network.",
            FailureKind.Filesystem => "Weir could not use a file or folder it needed.",
            FailureKind.Validation => "The saved settings or job payload did not pass validation.",
            FailureKind.NotFound => $"The item or endpoint{where} was not found.",
            _ => "The job hit an unexpected error.",
        };
    }

    public static string? NextActionForKind(FailureKind kind, string? provider, bool recoverable)
    {
        // The fallback is "provider", not "the provider": the templates already say "the ...", and an
        // unknown provider must not read "the the provider" (#540).
        var providerText = OperatorMessages.ProviderLabel(provider) is { Length: > 0 } label ? label : "provider";
        return kind switch
        {
            FailureKind.Credential or FailureKind.Auth => $"Re-enter the {providerText} credentials and run the connection test again.",
            FailureKind.Network => $"Check the {providerText} address, network access, and that the service is running.",
            FailureKind.RateLimit => "Wait for the provider limit to reset, or reduce how often this workflow runs.",
            FailureKind.Filesystem => "Check that the file or folder still exists and that Weir can read and write it.",
            FailureKind.Validation => "Review the saved settings for this workflow and save them again.",
            FailureKind.NotFound when !recoverable => "Refresh the source library and run the workflow again.",
            _ => null,
        };
    }

    /// <summary>Builds the operator-facing failure for an exception, with its technical detail redacted and capped at 1000 characters.</summary>
    public static OperatorFailure FromException(
        string module,
        string action,
        FailureSubject exception,
        string? provider = null,
        bool recoverable = false,
        string? continuation = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var kind = Classify(exception);
        var providerText = OperatorMessages.ProviderLabel(provider);
        var where = string.IsNullOrEmpty(providerText) ? string.Empty : $" for {providerText}";
        var state = recoverable ? "skipped and continued" : "failed";
        var happensNext = !string.IsNullOrEmpty(continuation)
            ? continuation
            : recoverable
                ? "Weir will continue with the next available provider."
                : "This job is marked failed so it does not look successful.";
        var message = $"{module} {action}{where} {state}: {WhyForKind(kind, provider)} {happensNext}";
        var detail = Diagnostics.SanitizeText("technical_detail", $"{exception.TypeName}: {exception.Message}");
        return new OperatorFailure(
            module, action, kind, recoverable, message, WhyForKind(kind, provider), happensNext,
            NextActionForKind(kind, provider, recoverable),
            PyStrings.Slice(detail, 1000));
    }

    /// <summary>A <c>RuntimeError</c> subject: the type name Weir's own refusals are recorded under.</summary>
    public static FailureSubject RuntimeError(string message) => new("RuntimeError", message, ExceptionCategory.Other);

    /// <summary>
    /// Maps a .NET exception to its stable failure type name and family. The name matters as well as the
    /// family: the classifier searches <c>"{type}: {message}"</c>, so <c>UnauthorizedAccessException</c>
    /// would read as an auth failure where <c>PermissionError</c> reads as a file problem.
    /// </summary>
    public static FailureSubject FromDotNet(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            Jobs.AlreadyRecordedFailureException => new FailureSubject(Jobs.AlreadyRecordedFailureException.PythonTypeName, exception.Message, ExceptionCategory.Other),
            FileNotFoundException or DirectoryNotFoundException => new FailureSubject("FileNotFoundError", exception.Message, ExceptionCategory.Filesystem),
            UnauthorizedAccessException => new FailureSubject("PermissionError", exception.Message, ExceptionCategory.Filesystem),
            TimeoutException => new FailureSubject("TimeoutError", exception.Message, ExceptionCategory.NetworkOrOs),
            System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException => new FailureSubject("ConnectionError", exception.Message, ExceptionCategory.NetworkOrOs),
            IOException => new FailureSubject("OSError", exception.Message, ExceptionCategory.NetworkOrOs),
            PyTypeErrorException => new FailureSubject("TypeError", exception.Message, ExceptionCategory.Validation),
            ArgumentException or FormatException or InvalidCastException or PyValueErrorException =>
                new FailureSubject("ValueError", exception.Message, ExceptionCategory.Validation),
            _ => new FailureSubject(exception.GetType().Name, exception.Message, ExceptionCategory.Other),
        };
    }
}

/// <summary>Guards for reported metric counts: never negative.</summary>
public static class MetricsTruth
{
    public static void RequireNonNegative(IReadOnlyDictionary<string, long> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        foreach (var (name, value) in counts)
        {
            if (value < 0)
            {
                throw new PyValueErrorException($"Metric {PyStrings.Repr(name)} must not be negative.");
            }
        }
    }

    public static long FinalizedSuccessTotal(IReadOnlyDictionary<string, long> counts)
    {
        RequireNonNegative(counts);
        return counts.Values.Sum();
    }
}
