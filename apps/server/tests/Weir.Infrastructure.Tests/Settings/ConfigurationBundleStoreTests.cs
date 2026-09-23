using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Settings;

/// <summary>The metadata provider key never leaves in an export.</summary>
public sealed class ConfigurationBundleStoreTests : IDisposable
{
    private readonly StoreFixture _store = new();
    private readonly ITimeZoneResolver _zones = new IanaTimeZoneResolver();

    public void Dispose() => _store.Dispose();

    private Task SeedMetadataProviderKeyAsync() =>
        _store.Execute("UPDATE suite_settings SET metadata_provider = 'tmdb', metadata_provider_key_ciphertext = 'the-ciphertext' WHERE id = 1");

    [Fact]
    public async Task An_export_omits_the_metadata_provider_key_ciphertext()
    {
        await SeedMetadataProviderKeyAsync();

        var bundle = await _store.WithUnitOfWork(ConfigurationBundleStore.BuildAsync, commit: false);
        var suiteSettings = (PyDict)bundle["suite_settings"];

        Assert.False(suiteSettings.ContainsKey("metadata_provider_key_ciphertext"));
        Assert.Equal("tmdb", ((PyStr)suiteSettings["metadata_provider"]).Value);
    }

    [Fact]
    public async Task Importing_a_bundle_with_no_metadata_provider_key_keeps_the_one_already_saved()
    {
        await SeedMetadataProviderKeyAsync();
        var bundle = await _store.WithUnitOfWork(ConfigurationBundleStore.BuildAsync, commit: false);

        await _store.WithUnitOfWork(async uow =>
        {
            await ConfigurationBundleStore.ApplyAsync(uow, bundle, _zones);
            return true;
        });

        Assert.Equal(1, await _store.Scalar("SELECT count(*) FROM suite_settings WHERE id = 1 AND metadata_provider_key_ciphertext = 'the-ciphertext'"));
    }
}
