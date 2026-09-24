using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Auth;

/// <summary>The API-facing session/user projections, listing a user's active sessions, and revoking them.</summary>
public sealed partial class AuthService
{
    /// <summary>The session as the API shows it.</summary>
    public WireObject SessionPublic(UserSessionRecord session, bool current)
    {
        ArgumentNullException.ThrowIfNull(session);
        var label = string.IsNullOrEmpty(session.ClientLabel) ? SessionRules.DefaultClientLabel : session.ClientLabel;
        return new WireObject()
            .Set("session_id", session.PublicId)
            .Set("client_label", WireStrings.Slice(label, 80))
            .Set("current", current)
            .Set("trusted_device", session.IsTrustedDevice)
            .Set("created_at", session.CreatedAt.ToWireText())
            .Set("last_seen_at", session.LastSeenAt.ToWireText())
            .Set("absolute_expires_at", session.AbsoluteExpiresAt.ToWireText())
            .Set("idle_timeout_minutes", SessionRules.IdleTimeoutMinutes(session.IsTrustedDevice, _options))
            .Set("absolute_timeout_days", SessionRules.AbsoluteTimeoutDays(session.IsTrustedDevice, _options));
    }

    public static WireObject UserPublic(UserRecord user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new WireObject().Set("id", user.Id).Set("username", user.Username).Set("role", user.Role);
    }

    /// <summary>The user's active sessions, newest first, with the request's own session as it is held in memory.</summary>
    public async Task<List<WireValue>> ListActiveSessionsAsync(UnitOfWork uow, long userId, UserSessionRecord? current)
    {
        var moment = Now();
        var rows = await AuthStore.ActiveSessionsNewestFirstAsync(uow, userId, moment).ConfigureAwait(false);
        var output = new List<WireValue>();
        foreach (var stored in rows)
        {
            var row = current is not null && stored.Id == current.Id ? current : stored;
            var idle = SessionRules.EffectiveIdleTimeout(row.IsTrustedDevice, _options);
            if (SessionRules.InvalidReason(row, idle, moment.AsUtc) is not null)
            {
                continue;
            }

            output.Add(SessionPublic(row, current is not null && row.Id == current.Id));
        }

        return output;
    }

    /// <summary>Revokes one of the user's sessions; false when it does not exist or is already revoked.</summary>
    public async Task<bool> RevokeUserSessionAsync(UnitOfWork uow, long userId, Guid sessionId)
    {
        var row = await AuthStore.FindUserSessionAsync(uow, userId, sessionId.ToString("N")).ConfigureAwait(false);
        if (row is null || row.RevokedAt is not null)
        {
            return false;
        }

        await AuthStore.RevokeSessionAsync(uow, row.Id, Now()).ConfigureAwait(false);
        return true;
    }

    /// <summary>Revokes every active session of the user except the current one.</summary>
    public async Task<int> RevokeOtherUserSessionsAsync(UnitOfWork uow, long userId, string? currentSessionId)
    {
        var moment = Now();
        var count = 0;
        foreach (var row in await AuthStore.ActiveSessionsAsync(uow, userId, moment).ConfigureAwait(false))
        {
            if (currentSessionId is not null && row.Id == currentSessionId)
            {
                continue;
            }

            await AuthStore.RevokeSessionAsync(uow, row.Id, moment).ConfigureAwait(false);
            count++;
        }

        return count;
    }
}
