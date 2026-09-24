using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Notifications;
using Weir.Core.Security;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>A download client connection could not be saved as asked, with an operator-readable reason.</summary>
public sealed class DownloadClientConnectionException : Exception
{
    public DownloadClientConnectionException()
    {
    }

    public DownloadClientConnectionException(string message)
        : base(message)
    {
    }

    public DownloadClientConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reading and writing download client connections with their secrets encrypted (#768). Mirrors
/// <see cref="MediaManagerConnectionService"/>, but simpler: no lanes, no webhook secret, and a connection can be
/// usable with no credential at all (an open Transmission needs neither a username nor a password).
/// </summary>
public sealed class DownloadClientConnectionService
{
    private const string NeedsNameMessage = "Give the connection a name so you can tell it apart later.";

    private readonly CredentialCipher _cipher;

    public DownloadClientConnectionService(CredentialCipher cipher)
    {
        _cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
    }

    public CredentialCipher Cipher => _cipher;

    /// <summary>Create a connection. Throws <see cref="DownloadClientConnectionException"/> for an operator mistake.</summary>
    public async Task<long> CreateAsync(
        UnitOfWork uow, string kind, string name, string baseUrl = "", string? username = null, string? password = null, string? apiKey = null, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var label = PyStrings.Strip(name ?? string.Empty);
        if (label.Length == 0)
        {
            throw new DownloadClientConnectionException(NeedsNameMessage);
        }

        if (await DownloadClientConnectionStore.NameExistsAsync(uow, label).ConfigureAwait(false))
        {
            throw new DownloadClientConnectionException($"A connection named {PyStrings.Repr(label)} already exists.");
        }

        var validKind = ValidateKind(kind);
        var validUrl = ValidateBaseUrl(baseUrl);
        var trimmedUsername = PyStrings.Strip(username ?? string.Empty);
        return await DownloadClientConnectionStore.InsertAsync(
            uow,
            validKind,
            label,
            enabled,
            validUrl,
            trimmedUsername.Length > 0 ? trimmedUsername : null,
            EncryptOrNull(password),
            EncryptOrNull(apiKey)).ConfigureAwait(false);
    }

    /// <summary>Update a connection: <see langword="null"/> leaves a field alone; an empty secret clears it.</summary>
    public async Task UpdateAsync(
        UnitOfWork uow,
        DownloadClientConnectionRecord row,
        string? name = null,
        string? baseUrl = null,
        string? username = null,
        string? password = null,
        string? apiKey = null,
        bool? enabled = null)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(row);
        var changes = new List<(string Column, object? Value)>();
        if (name is not null)
        {
            var label = PyStrings.Strip(name);
            if (label.Length == 0)
            {
                throw new DownloadClientConnectionException(NeedsNameMessage);
            }

            if (await DownloadClientConnectionStore.NameExistsAsync(uow, label, row.Id).ConfigureAwait(false))
            {
                throw new DownloadClientConnectionException($"A connection named {PyStrings.Repr(label)} already exists.");
            }

            if (label != row.Name)
            {
                changes.Add(("name", label));
            }
        }

        string? validatedBaseUrl = null;
        if (baseUrl is not null)
        {
            validatedBaseUrl = ValidateBaseUrl(baseUrl);
            if (validatedBaseUrl != row.BaseUrl)
            {
                changes.Add(("base_url", validatedBaseUrl));
            }
        }

        if (enabled is { } flag && flag != row.Enabled)
        {
            changes.Add(("enabled", flag ? 1 : 0));
        }

        // Going live must still have an address ExternalUrlPolicy accepts, even when this call only flips
        // `enabled`: the stored value could predate this check, or come from a restored backup.
        if (enabled == true)
        {
            ValidateBaseUrl(validatedBaseUrl ?? row.BaseUrl);
        }

        if (username is not null)
        {
            var trimmed = PyStrings.Strip(username);
            var value = trimmed.Length > 0 ? trimmed : null;
            if (value != row.Username)
            {
                changes.Add(("username", value));
            }
        }

        if (password is not null)
        {
            var ciphertext = EncryptOrNull(password);
            if (ciphertext != row.PasswordCiphertext)
            {
                changes.Add(("password_ciphertext", ciphertext));
            }
        }

        if (apiKey is not null)
        {
            var ciphertext = EncryptOrNull(apiKey);
            if (ciphertext != row.ApiKeyCiphertext)
            {
                changes.Add(("api_key_ciphertext", ciphertext));
            }
        }

        await DownloadClientConnectionStore.UpdateColumnsAsync(uow, row.Id, changes).ConfigureAwait(false);
    }

    /// <summary>
    /// The usable connection for a row: null only when it has no address. Unlike a media manager, several
    /// dialects here need no credential at all, so a missing username/password/key does not disqualify a row.
    /// </summary>
    public DownloadClientConnection? ConnectionFromRow(DownloadClientConnectionRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var url = PyStrings.Strip(row.BaseUrl);
        if (url.Length == 0)
        {
            return null;
        }

        var password = string.IsNullOrEmpty(row.PasswordCiphertext) ? null : _cipher.Decrypt(row.PasswordCiphertext);
        var apiKey = string.IsNullOrEmpty(row.ApiKeyCiphertext) ? null : _cipher.Decrypt(row.ApiKeyCiphertext);
        return new DownloadClientConnection(row.Kind, row.Name, url, row.Username, password, apiKey, row.Id);
    }

    private string? EncryptOrNull(string? plaintext)
    {
        var stripped = PyStrings.Strip(plaintext ?? string.Empty);
        return stripped.Length == 0 ? null : Encrypt(stripped);
    }

    /// <summary>
    /// Encryption throws a plain <see cref="PyValueErrorException"/> when no <c>WEIR_CREDENTIALS_SECRET</c> or
    /// <c>WEIR_SESSION_SECRET</c> is configured, exactly like <see cref="MediaManagerConnectionService"/>'s
    /// <c>EncryptApiKey</c> (#544 item 3). It becomes a <see cref="DownloadClientConnectionException"/> so the
    /// operator gets a readable 400 naming the env var to set, not a 500.
    /// </summary>
    private string Encrypt(string plaintext)
    {
        try
        {
            return _cipher.Encrypt(plaintext);
        }
        catch (PyValueErrorException exception)
        {
            throw new DownloadClientConnectionException(exception.Message, exception);
        }
    }

    private static string ValidateKind(string? kind)
    {
        var value = PyStrings.Strip(kind ?? string.Empty).ToLowerInvariant();
        if (!DownloadClientKinds.All.Contains(value))
        {
            throw new DownloadClientConnectionException(
                $"Unknown download client kind {PyStrings.Repr(kind ?? string.Empty)}. Known kinds: {string.Join(", ", DownloadClientKinds.All)}.");
        }

        return value;
    }

    private static string ValidateBaseUrl(string? baseUrl)
    {
        var raw = PyStrings.Strip(baseUrl ?? string.Empty);
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return ExternalUrlPolicy.NormalizeLocalServiceBaseUrl(raw);
        }
        catch (PyValueErrorException exception)
        {
            throw new DownloadClientConnectionException($"That address will not work: {exception.Message}", exception);
        }
    }
}
