using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.Time;

namespace Weir.Core.Auth;

/// <summary>Values persisted in <c>users.role</c>.</summary>
public static class UserRoles
{
    public const string Admin = "admin";
    public const string Operator = "operator";
    public const string Viewer = "viewer";

    public static readonly IReadOnlySet<string> Valid = new HashSet<string>(StringComparer.Ordinal) { Admin, Operator, Viewer };

    public static readonly IReadOnlySet<string> AdminOnly = new HashSet<string>(StringComparer.Ordinal) { Admin };

    public static readonly IReadOnlySet<string> OperatorOrAdmin = new HashSet<string>(StringComparer.Ordinal) { Admin, Operator };
}

/// <summary>A <c>users</c> row.</summary>
public sealed record UserRecord(long Id, string Username, string PasswordHash, string Role, bool IsActive);

/// <summary>A <c>user_sessions</c> row. <see cref="Id"/> is the stored 32-character hex UUID.</summary>
public sealed record UserSessionRecord(
    string Id,
    long UserId,
    string TokenHash,
    PyDateTime CreatedAt,
    PyDateTime AbsoluteExpiresAt,
    bool IsTrustedDevice,
    PyDateTime LastSeenAt,
    PyDateTime? RevokedAt,
    string ClientLabel)
{
    /// <summary>The hyphenated UUID form the API shows.</summary>
    public string PublicId => Guid.TryParseExact(Id, "N", out var guid) ? guid.ToString("D") : Id;
}

/// <summary>Why a session does not authenticate.</summary>
public enum SessionInvalidReason
{
    Revoked,
    AbsoluteExpired,
    IdleExpired,
}

/// <summary>Pure rules for sign-in sessions: timeouts, validity, client labels and cookie flags.</summary>
public static class SessionRules
{
    public const int MaxActiveSessionsPerUser = 5;
    public const string DefaultClientLabel = "Browser session";

    public static TimeSpan EffectiveIdleTimeout(bool isTrustedDevice, WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Minutes(isTrustedDevice ? options.SessionTrustedIdleMinutes : options.SessionIdleMinutes);
    }

    public static long AbsoluteTimeoutDays(bool isTrustedDevice, WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return isTrustedDevice ? options.SessionTrustedAbsoluteDays : options.SessionAbsoluteDays;
    }

    public static long IdleTimeoutMinutes(bool isTrustedDevice, WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return isTrustedDevice ? options.SessionTrustedIdleMinutes : options.SessionIdleMinutes;
    }

    public static SessionInvalidReason? InvalidReason(UserSessionRecord row, TimeSpan idle, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.RevokedAt is not null)
        {
            return SessionInvalidReason.Revoked;
        }

        if (nowUtc >= row.AbsoluteExpiresAt.AsUtc)
        {
            return SessionInvalidReason.AbsoluteExpired;
        }

        return nowUtc > SafeAdd(row.LastSeenAt.AsUtc, idle) ? SessionInvalidReason.IdleExpired : null;
    }

    /// <summary>How stale <c>last_seen_at</c> may get before a request rewrites it: at most 60 s, at most half the idle window.</summary>
    public static TimeSpan LastSeenTouchGap(TimeSpan idle)
    {
        var half = TimeSpan.FromTicks(idle.Ticks / 2);
        var cap = TimeSpan.FromSeconds(60);
        return half < cap ? half : cap;
    }

    public static TimeSpan Minutes(long minutes) =>
        minutes > TimeSpan.MaxValue.TotalMinutes ? TimeSpan.MaxValue : TimeSpan.FromMinutes(minutes);

    public static TimeSpan Days(long days) =>
        days > TimeSpan.MaxValue.TotalDays ? TimeSpan.MaxValue : TimeSpan.FromDays(days);

    public static DateTime SafeAdd(DateTime value, TimeSpan span) =>
        span.Ticks > DateTime.MaxValue.Ticks - value.Ticks ? DateTime.MaxValue : value + span;

    public static DateTime SafeSubtract(DateTime value, TimeSpan span) =>
        span.Ticks > value.Ticks ? DateTime.MinValue : value - span;

    /// <summary>A coarse, non-identifying label from the user agent, such as "Chrome on Windows".</summary>
    public static string ClientLabelFromUserAgent(string? userAgent)
    {
        var value = (userAgent ?? string.Empty).ToLowerInvariant();
        var browser = "Browser";
        if (value.Contains("edg/", StringComparison.Ordinal) || value.Contains("edge/", StringComparison.Ordinal))
        {
            browser = "Edge";
        }
        else if (value.Contains("chrome/", StringComparison.Ordinal) || value.Contains("crios/", StringComparison.Ordinal))
        {
            browser = "Chrome";
        }
        else if (value.Contains("firefox/", StringComparison.Ordinal) || value.Contains("fxios/", StringComparison.Ordinal))
        {
            browser = "Firefox";
        }
        else if (value.Contains("safari/", StringComparison.Ordinal) && !value.Contains("chrome/", StringComparison.Ordinal))
        {
            browser = "Safari";
        }
        else if (value.Contains("electron/", StringComparison.Ordinal))
        {
            browser = "Weir app";
        }

        var platform = "device";
        if (value.Contains("windows", StringComparison.Ordinal))
        {
            platform = "Windows";
        }
        else if (value.Contains("mac os", StringComparison.Ordinal) || value.Contains("macintosh", StringComparison.Ordinal))
        {
            platform = "macOS";
        }
        else if (value.Contains("android", StringComparison.Ordinal))
        {
            platform = "Android";
        }
        else if (value.Contains("iphone", StringComparison.Ordinal) || value.Contains("ipad", StringComparison.Ordinal) || value.Contains("ios", StringComparison.Ordinal))
        {
            platform = "iOS";
        }
        else if (value.Contains("linux", StringComparison.Ordinal))
        {
            platform = "Linux";
        }

        return PyStrings.Slice($"{browser} on {platform}", 80);
    }

    /// <summary>Whether the session cookie gets <c>Secure</c>: in auto mode, only for a request that arrived over HTTPS.</summary>
    public static bool ResolveCookieSecure(string? requestScheme, CookieSecureMode mode) => mode switch
    {
        CookieSecureMode.Always => true,
        CookieSecureMode.Never => false,
        _ => string.Equals((requestScheme ?? string.Empty).Trim(), "https", StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>
    /// Whether the cookie could not be marked Secure only because nothing told Weir this request really arrived
    /// over TLS: the connection itself reads as plain http, but the browser's own Origin or Referer is https,
    /// and no trusted proxy is configured to translate an <c>X-Forwarded-Proto</c> header into the real scheme
    /// (<c>WEIR_TRUSTED_PROXY_IPS</c>). A plain LAN http request, with no https evidence, never triggers this.
    /// </summary>
    public static bool CookieSecureBlockedByUntranslatedTls(CookieSecureMode mode, string? requestScheme, string? origin, string? referer, bool hasTrustedProxy)
    {
        if (mode != CookieSecureMode.Auto || hasTrustedProxy ||
            !string.Equals((requestScheme ?? string.Empty).Trim(), "http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsHttpsUrl(origin) || IsHttpsUrl(referer);
    }

    /// <summary>A cheap, exception-free check: good enough to notice https evidence in an untrusted header,
    /// without fully parsing (and so having to reject) a malformed one.</summary>
    private static bool IsHttpsUrl(string? value) => value is not null && value.AsSpan().TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static string SameSiteText(CookieSameSite sameSite) => sameSite switch
    {
        CookieSameSite.Strict => "strict",
        CookieSameSite.None => "none",
        _ => "lax",
    };

    public static string SecureModeText(CookieSecureMode mode) => mode switch
    {
        CookieSecureMode.Always => "always",
        CookieSecureMode.Never => "never",
        _ => "auto",
    };
}
