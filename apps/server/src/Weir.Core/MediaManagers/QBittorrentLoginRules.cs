namespace Weir.Core.MediaManagers;

/// <summary>What qBittorrent's login answer means: whether it was accepted, and the session cookie to send on later calls.</summary>
public readonly record struct QBittorrentLogin(bool Accepted, string? SessionCookie)
{
    public static QBittorrentLogin Refused { get; } = new(false, null);
}

/// <summary>
/// Reads the answer to <c>POST /api/v2/auth/login</c> with no I/O. Older versions answer 200 with <c>Ok.</c> or
/// <c>Fails.</c> and name the session cookie <c>SID</c>. 5.x answers a good login with 204 and an empty body and
/// names the cookie <c>QBT_SID_&lt;port&gt;</c>, so the cookie is taken by whatever name it carries. An empty body
/// is a good login whether or not a cookie came with it: a server answers 204 without one when it already trusts the
/// caller, for example when the login carried a live session (#842, #856).
/// </summary>
public static class QBittorrentLoginRules
{
    private const string AcceptedBody = "Ok.";

    public static QBittorrentLogin Read(int status, string body, string? setCookie)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (status is < 200 or >= 300)
        {
            return QBittorrentLogin.Refused;
        }

        var cookie = SessionCookie(setCookie);
        var text = body.Trim();
        var accepted = text.Length == 0 || string.Equals(text, AcceptedBody, StringComparison.Ordinal);
        return accepted ? new QBittorrentLogin(true, cookie) : QBittorrentLogin.Refused;
    }

    /// <summary>The leading <c>name=value</c> of the first <c>Set-Cookie</c>, without its attributes, or null when there is none.</summary>
    private static string? SessionCookie(string? setCookie)
    {
        var pair = setCookie?.Split(';', 2)[0].Trim();
        if (pair is null)
        {
            return null;
        }

        var separator = pair.IndexOf('=', StringComparison.Ordinal);
        return separator > 0 && separator < pair.Length - 1 ? pair : null;
    }
}
