using Weir.Core.Configuration;
using Weir.Core.Rules;
using Weir.Core.Security;
using Weir.Core.Settings;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>The Processing metadata-provider connection view.</summary>
public sealed record MetadataProviderView(string Provider, string BaseUrl, bool KeyConfigured, IReadOnlyList<string> KnownProviders);

/// <summary>
/// Metadata provider settings: the key is stored encrypted and never returned. <c>POST /test</c> asks the real provider through
/// <see cref="Weir.Infrastructure.MediaManagers.MetadataProviderService"/> (#520) when a key is configured;
/// <see cref="Test"/> here only covers the not-configured answer.
/// </summary>
public static class MetadataProviderStore
{
    public const string DefaultTmdbBaseUrl = "https://api.themoviedb.org/3";

    public static readonly IReadOnlyList<string> KnownProviders = ["tmdb"];

    public static MetadataProviderView View(SuiteSettingsRecord row) => new(
        row.MetadataProvider,
        string.IsNullOrWhiteSpace(row.MetadataProviderBaseUrl) ? DefaultTmdbBaseUrl : row.MetadataProviderBaseUrl.Trim(),
        !string.IsNullOrWhiteSpace(row.MetadataProviderKeyCiphertext),
        KnownProviders);

    /// <summary>Encrypts the provider key with the same cipher the manager credentials use.</summary>
    public static string EncryptKey(WeirOptions options, string plaintext, TimeProvider time)
    {
        if (plaintext.Trim().Length == 0)
        {
            return string.Empty;
        }

        var cipher = new CredentialCipher(options.CredentialsSecret, options.SessionSecret, [], time);
        return cipher.Encrypt(plaintext);
    }

    public static async Task<SuiteSettingsRecord> ApplyAsync(
        UnitOfWork uow, SuiteSettingsStore suiteSettings, WeirOptions options, TimeProvider time, string provider, string baseUrl, string? apiKey)
    {
        var before = await suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        var after = before with
        {
            MetadataProvider = provider,
            MetadataProviderBaseUrl = baseUrl.Trim(),
            MetadataProviderKeyCiphertext = apiKey is null ? before.MetadataProviderKeyCiphertext : EncryptKey(options, apiKey, time),
        };
        await suiteSettings.UpdateAsync(uow, before, after).ConfigureAwait(false);
        return await suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
    }

    /// <summary>
    /// The provider test's not-configured answer: no key means no manager can be asked, so the endpoint
    /// reports this honestly instead of calling <see cref="Weir.Infrastructure.MediaManagers.MetadataProviderService"/>
    /// (which needs a saved key). When a key is configured, the endpoint asks that service instead.
    /// </summary>
    public static LookupResult Test(SuiteSettingsRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return new LookupResult
        {
            Status = LookupResult.StatusNotConfigured,
            Detail = "No metadata provider is configured, so Weir uses the language preferences on each rule set.",
        };
    }
}
