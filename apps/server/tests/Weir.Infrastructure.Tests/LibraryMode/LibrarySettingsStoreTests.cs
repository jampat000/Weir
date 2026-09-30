using Weir.Core.LibraryMode;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.LibraryMode;

public sealed class LibrarySettingsStoreTests : IDisposable
{
    private readonly JobsTestDatabase _db = new();
    private readonly LibrarySettingsStore _store = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_library_with_no_settings_row_has_735_off_and_no_originals_folder()
    {
        var libraryId = _db.AddLibrary();
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);

        var settings = await _store.GetAsync(uow, libraryId);

        Assert.False(settings.KeepOriginalAfterClean);
        Assert.Equal(string.Empty, settings.OriginalsFolder);
    }

    [Fact]
    public async Task Keep_original_after_clean_and_its_folder_round_trip()
    {
        var libraryId = _db.AddLibrary();
        var settings = new LibrarySettings(
            ["/srv/in"], ScheduleEnabled: false, KeepOriginalAfterClean: true, OriginalsFolder: "/srv/originals");
        await using (var uow = await UnitOfWork.OpenAsync(_db.Database))
        {
            await _store.SetAsync(uow, libraryId, settings);
            await uow.CommitAsync();
        }

        await using var read = await UnitOfWork.OpenAsync(_db.Database);
        var restored = await _store.GetAsync(read, libraryId);

        Assert.True(restored.KeepOriginalAfterClean);
        Assert.Equal("/srv/originals", restored.OriginalsFolder);
    }

    [Fact]
    public async Task A_library_follows_its_workflows_profile_until_it_chooses_its_own()
    {
        var libraryId = _db.AddLibrary();
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);

        var settings = await _store.GetAsync(uow, libraryId);

        Assert.Null(settings.RuleSetId);
    }

    [Fact]
    public async Task The_rules_profile_a_library_chooses_round_trips_and_can_be_cleared()
    {
        var libraryId = _db.AddLibrary();
        var profileId = _db.AddRuleSet("Cleaning");
        await using (var uow = await UnitOfWork.OpenAsync(_db.Database))
        {
            await _store.SetAsync(uow, libraryId, new LibrarySettings(["/srv/in"], ScheduleEnabled: false, RuleSetId: profileId));
            await uow.CommitAsync();
        }

        await using (var read = await UnitOfWork.OpenAsync(_db.Database))
        {
            Assert.Equal(profileId, (await _store.GetAsync(read, libraryId)).RuleSetId);
            await _store.SetAsync(read, libraryId, (await _store.GetAsync(read, libraryId)) with { RuleSetId = null });
            await read.CommitAsync();
        }

        await using var cleared = await UnitOfWork.OpenAsync(_db.Database);
        Assert.Null((await _store.GetAsync(cleared, libraryId)).RuleSetId);
    }

    [Fact]
    public async Task Switching_the_setting_off_clears_nothing_else()
    {
        var libraryId = _db.AddLibrary();
        await using (var uow = await UnitOfWork.OpenAsync(_db.Database))
        {
            await _store.SetAsync(
                uow, libraryId, new LibrarySettings(["/srv/in"], ScheduleEnabled: false, KeepOriginalAfterClean: true, OriginalsFolder: "/srv/originals"));
            await uow.CommitAsync();
        }

        await using (var uow = await UnitOfWork.OpenAsync(_db.Database))
        {
            var existing = await _store.GetAsync(uow, libraryId);
            await _store.SetAsync(uow, libraryId, existing with { KeepOriginalAfterClean = false });
            await uow.CommitAsync();
        }

        await using var read = await UnitOfWork.OpenAsync(_db.Database);
        var restored = await _store.GetAsync(read, libraryId);
        Assert.False(restored.KeepOriginalAfterClean);
        // The folder a person typed is kept even while the switch is off, so turning it back on remembers it.
        Assert.Equal("/srv/originals", restored.OriginalsFolder);
    }
}
