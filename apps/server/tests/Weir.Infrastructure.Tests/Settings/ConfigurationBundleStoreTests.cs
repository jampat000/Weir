using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Settings;

/// <summary>
/// The metadata provider key never leaves in an export. #723: restoring a configuration bundle also runs
/// every library row through the same validation a create/update request does — folder overlap and the
/// folder-safety rules, name uniqueness, and the closed enumerations — before any row is written, so a bad
/// row refuses the whole restore instead of leaving the library table partly replaced.
/// </summary>
public sealed class ConfigurationBundleStoreTests : IDisposable
{
    private readonly StoreFixture _store = new();
    private readonly ITimeZoneResolver _zones = new IanaTimeZoneResolver();

    public void Dispose() => _store.Dispose();

    private Task SeedMetadataProviderKeyAsync() =>
        _store.Execute("UPDATE suite_settings SET metadata_provider = 'tmdb', metadata_provider_key_ciphertext = 'the-ciphertext' WHERE id = 1");

    private Task<PyDict> BuildBundleAsync() => _store.WithUnitOfWork(ConfigurationBundleStore.BuildAsync, commit: false);

    private Task<bool> ApplyAsync(PyDict bundle) => _store.WithUnitOfWork(async uow =>
    {
        await ConfigurationBundleStore.ApplyAsync(uow, bundle, _zones, _store.Options.WeirHome);
        return true;
    });

    private static PyDict LibraryAt(PyDict bundle, int index) => (PyDict)((PyList)bundle["libraries"]).Items[index];

    [Fact]
    public async Task An_export_omits_the_metadata_provider_key_ciphertext()
    {
        await SeedMetadataProviderKeyAsync();

        var bundle = await BuildBundleAsync();
        var suiteSettings = (PyDict)bundle["suite_settings"];

        Assert.False(suiteSettings.ContainsKey("metadata_provider_key_ciphertext"));
        Assert.Equal("tmdb", ((PyStr)suiteSettings["metadata_provider"]).Value);
    }

    [Fact]
    public async Task Importing_a_bundle_with_no_metadata_provider_key_keeps_the_one_already_saved()
    {
        await SeedMetadataProviderKeyAsync();
        var bundle = await BuildBundleAsync();

        await ApplyAsync(bundle);

        Assert.Equal(1, await _store.Scalar("SELECT count(*) FROM suite_settings WHERE id = 1 AND metadata_provider_key_ciphertext = 'the-ciphertext'"));
    }

    [Fact]
    public async Task A_watched_folder_with_no_output_folder_refuses_the_whole_restore()
    {
        var bundle = await BuildBundleAsync();
        LibraryAt(bundle, 0).Set("watched_folder", new PyStr(@"C:\media\watched"));

        var exception = await Assert.ThrowsAsync<PyValueErrorException>(() => ApplyAsync(bundle));

        Assert.Contains("Movies", exception.Message, StringComparison.Ordinal);
        Assert.Contains("output folder", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overlapping_folders_between_two_restored_libraries_refuse_the_whole_restore()
    {
        var bundle = await BuildBundleAsync();
        LibraryAt(bundle, 0).Set("watched_folder", new PyStr(@"C:\media\shared\in"));
        LibraryAt(bundle, 0).Set("output_folder", new PyStr(@"C:\media\shared\out"));
        LibraryAt(bundle, 1).Set("watched_folder", new PyStr(@"C:\media\shared\out\nested"));
        LibraryAt(bundle, 1).Set("output_folder", new PyStr(@"C:\media\tv-out"));

        var exception = await Assert.ThrowsAsync<PyValueErrorException>(() => ApplyAsync(bundle));

        Assert.Contains("overlap", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_rejected_file_action_refuses_the_whole_restore()
    {
        var bundle = await BuildBundleAsync();
        LibraryAt(bundle, 0).Set("rejected_file_action", new PyStr("quarantine"));

        var exception = await Assert.ThrowsAsync<PyValueErrorException>(() => ApplyAsync(bundle));

        Assert.Contains("rejected file action", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_library_folder_inside_weirs_own_data_folder_refuses_the_whole_restore()
    {
        var bundle = await BuildBundleAsync();
        var insideWeirHome = Path.Combine(_store.Options.WeirHome, "watched");
        LibraryAt(bundle, 0).Set("watched_folder", new PyStr(insideWeirHome));
        LibraryAt(bundle, 0).Set("output_folder", new PyStr(Path.Combine(_store.Options.WeirHome, "output")));

        var exception = await Assert.ThrowsAsync<PyValueErrorException>(() => ApplyAsync(bundle));

        Assert.Contains("data folder", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_duplicate_library_name_refuses_the_whole_restore()
    {
        var bundle = await BuildBundleAsync();
        LibraryAt(bundle, 1).Set("name", new PyStr("Movies"));

        var exception = await Assert.ThrowsAsync<PyValueErrorException>(() => ApplyAsync(bundle));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bad_row_leaves_the_library_table_completely_untouched()
    {
        var bundle = await BuildBundleAsync();
        LibraryAt(bundle, 0).Set("watched_folder", new PyStr(@"C:\media\watched"));

        await Assert.ThrowsAsync<PyValueErrorException>(() => ApplyAsync(bundle));

        var names = await _store.WithUnitOfWork(
            uow => uow.QueryAsync("SELECT name FROM libraries ORDER BY id", reader => reader.GetString(0)),
            commit: false);
        Assert.Equal(["Movies", "TV"], names);
    }

    [Fact]
    public async Task A_valid_bundle_restores_without_complaint()
    {
        var bundle = await BuildBundleAsync();
        LibraryAt(bundle, 0).Set("watched_folder", new PyStr(@"C:\media\movies-in"));
        LibraryAt(bundle, 0).Set("output_folder", new PyStr(@"C:\media\movies-out"));

        await ApplyAsync(bundle);

        var watched = await _store.WithUnitOfWork(
            uow => uow.QueryAsync("SELECT watched_folder FROM libraries WHERE name = 'Movies'", reader => reader.GetString(0)),
            commit: false);
        Assert.Equal(@"C:\media\movies-in", Assert.Single(watched));
    }
}
