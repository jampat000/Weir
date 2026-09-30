using Weir.Core.LibraryMode;
using Weir.Core.Processing;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.LibraryMode;

/// <summary>Which rules profile scans, plans and cleans a library's existing files.</summary>
public sealed class LibraryModeRulesTests : IDisposable
{
    private readonly JobsTestDatabase _db = new();
    private readonly LibraryStore _libraries = new();

    public void Dispose() => _db.Dispose();

    private async Task<(ProcessingLibraryRecord Library, long WorkflowProfile, long OwnProfile)> LibraryWithTwoProfilesAsync()
    {
        var workflowProfile = _db.AddRuleSet("Workflow profile", audioLanguage: "jpn");
        var ownProfile = _db.AddRuleSet("Cleaning profile", audioLanguage: "fre");
        var libraryId = _db.AddLibrary();
        _db.Execute("UPDATE libraries SET rule_set_id = @profile WHERE id = @id", ("@profile", workflowProfile), ("@id", libraryId));
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);
        var library = await _libraries.GetAsync(uow, libraryId);
        return (library!, workflowProfile, ownProfile);
    }

    [Fact]
    public async Task A_library_that_has_not_chosen_a_profile_is_cleaned_by_its_workflows_profile()
    {
        var (library, workflowProfile, _) = await LibraryWithTwoProfilesAsync();
        var settings = new LibrarySettings([], ScheduleEnabled: false);
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);

        var rules = await LibraryModeRules.ForAsync(uow, _libraries, library, settings);

        Assert.Equal(workflowProfile, LibraryModeRules.EffectiveRuleSetId(library, settings));
        Assert.Equal("jpn", rules.PrimaryAudioLang);
    }

    [Fact]
    public async Task A_library_that_chose_a_profile_is_cleaned_by_it_instead_of_its_workflows()
    {
        var (library, _, ownProfile) = await LibraryWithTwoProfilesAsync();
        var settings = new LibrarySettings([], ScheduleEnabled: false, RuleSetId: ownProfile);
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);

        var rules = await LibraryModeRules.ForAsync(uow, _libraries, library, settings);

        Assert.Equal(ownProfile, LibraryModeRules.EffectiveRuleSetId(library, settings));
        Assert.Equal("fre", rules.PrimaryAudioLang);
    }

    [Fact]
    public async Task A_library_with_no_profile_anywhere_uses_the_shipped_defaults()
    {
        var libraryId = _db.AddLibrary();
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);
        var library = (await _libraries.GetAsync(uow, libraryId))!;

        var rules = await LibraryModeRules.ForAsync(uow, _libraries, library, new LibrarySettings([], ScheduleEnabled: false));

        Assert.Equal(RuleSetConversion.ToRulesConfig(null).PrimaryAudioLang, rules.PrimaryAudioLang);
    }
}
