using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Security;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Auth;

/// <summary>Changing a signed-in user's own credentials, and first-admin bootstrap.</summary>
public sealed partial class AuthService
{
    /// <summary>Changes the username after checking the current password. Throws <see cref="WireValueException"/> with the operator message.</summary>
    public async Task<string> ChangeUsernameAsync(UnitOfWork uow, long userId, string currentPassword, string newUsername)
    {
        var user = await _users.GetUserAsync(uow, userId).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            throw new WireValueException("Account is not available.");
        }

        if (!await VerifyPasswordAsync(currentPassword, user.PasswordHash).ConfigureAwait(false))
        {
            throw new WireValueException("Current password is incorrect.");
        }

        var candidate = (newUsername ?? string.Empty).Trim();
        if (candidate.Length == 0)
        {
            throw new WireValueException("Username is required.");
        }

        if (candidate == user.Username)
        {
            throw new WireValueException("New username must be different from the current username.");
        }

        var clash = await _users.FindUserByLowerUsernameAsync(uow, candidate.ToLowerInvariant()).ConfigureAwait(false);
        if (clash is not null && clash.Id != user.Id)
        {
            throw new WireValueException("That username is already taken.");
        }

        await _users.UpdateUsernameAsync(uow, user.Id, candidate).ConfigureAwait(false);
        return candidate;
    }

    /// <summary>Changes the password: rotate the hash and revoke every active session.</summary>
    public async Task ChangePasswordAsync(UnitOfWork uow, long userId, string currentPassword, string newPassword)
    {
        var user = await _users.GetUserAsync(uow, userId).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            throw new WireValueException("Account is not available.");
        }

        if (!await VerifyPasswordAsync(currentPassword, user.PasswordHash).ConfigureAwait(false))
        {
            throw new WireValueException("Current password is incorrect.");
        }

        if (currentPassword == newPassword)
        {
            throw new WireValueException("New password must be different from the current password.");
        }

        if (PasswordPolicy.Validate(newPassword, user.Username) is { } problem)
        {
            throw new WireValueException(problem);
        }

        await _users.UpdatePasswordHashAsync(uow, user.Id, PasswordHasher.Hash(newPassword)).ConfigureAwait(false);
        await _users.RevokeActiveSessionsForUserAsync(uow, user.Id, Now()).ConfigureAwait(false);
    }

    /// <summary>Bootstrap is allowed while no usable (active) admin exists.</summary>
    public async Task<bool> BootstrapAllowedAsync(UnitOfWork uow) =>
        await _users.CountActiveAdminsAsync(uow).ConfigureAwait(false) == 0;

    /// <summary>
    /// Creates the first admin: validate the password, clear any inactive admin rows and insert the
    /// admin. A username clash surfaces as a <see cref="Microsoft.Data.Sqlite.SqliteException"/> constraint error.
    /// </summary>
    public async Task<UserRecord> CreateInitialAdminAsync(UnitOfWork uow, string username, string password)
    {
        if (!await BootstrapAllowedAsync(uow).ConfigureAwait(false))
        {
            throw new InvalidOperationException(BootstrapNotAllowedMessage);
        }

        if (PasswordPolicy.Validate(password, username) is { } problem)
        {
            throw new WireValueException(problem);
        }

        await _users.DeleteAdminsAsync(uow).ConfigureAwait(false);
        var trimmed = username.Trim();
        var hash = PasswordHasher.Hash(password);
        var id = await _users.InsertUserAsync(uow, trimmed, hash, UserRoles.Admin, isActive: true).ConfigureAwait(false);
        return new UserRecord(id, trimmed, hash, UserRoles.Admin, true);
    }
}
