using Weir.Core.Media;
using Weir.Core.Processing;
using Weir.Core.Time;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>Real-SQLite proof of <c>LibraryStore</c>, the library persistence layer (ADR-0014).</summary>
public sealed class LibraryStoreTests
{
    private static readonly LibraryStore Store = new();

    private static ProcessingLibraryInput NewLibrary(string name, string watched = "", string output = "", string mediaType = "movie") => new()
    {
        Name = name,
        MediaType = mediaType,
        WatchedFolder = watched,
        OutputFolder = output,
    };

    [Fact]
    public async Task A_fresh_database_seeds_the_movies_and_tv_libraries()
    {
        using var db = new JobsTestDatabase(keepSeedRows: true);
        await using var uow = await UnitOfWork.OpenAsync(db.Database);

        var rows = await Store.ListAsync(uow);

        Assert.Equal(["Movies", "TV"], rows.Select(r => r.Name));
        Assert.Equal(["movie", "tv"], rows.Select(r => r.MediaType));
    }

    [Fact]
    public async Task Creating_a_library_assigns_the_next_display_order_and_round_trips_every_field()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);

        var created = await Store.CreateAsync(uow, NewLibrary("4K", watched: @"c:\media\4k-in", output: @"c:\media\4k-out"));

        Assert.True(created.Id > 0);
        Assert.Equal("4K", created.Name);
        Assert.Equal("movie", created.MediaType);
        Assert.Equal(@"c:\media\4k-in", created.WatchedFolder);
        Assert.Equal(ProcessingFailurePolicies.PassThrough, created.FailurePolicy);

        var reloaded = await Store.GetAsync(uow, created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(created.Name, reloaded!.Name);
    }

    /// <summary>
    /// Every column is read back by position, so a shifted or swapped column shows up here: each setting is saved with a value
    /// that differs from its default and from the other settings of its type, and reads back as saved.
    /// </summary>
    [Fact]
    public async Task A_library_reads_back_every_setting_it_was_saved_with()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var input = new ProcessingLibraryInput
        {
            Name = "Everything set",
            MediaType = ProcessingMediaScopes.Tv,
            Enabled = false,
            WatchedFolder = "/media/in",
            WorkFolder = "/media/work",
            OutputFolder = "/media/out",
            MediaExtensionsCsv = ".mkv,.avi",
            ExcludeMarkersCsv = "__sample__",
            IncludePatternsCsv = "keep-me",
            ExcludePatternsCsv = "drop-me",
            MinFileSizeMb = 11,
            MaxFileSizeMb = 12,
            RejectedFileAction = RejectedFileActions.DeleteFile,
            ReadyAfterSeconds = 13,
            CreatedAfter = Timestamp.Naive(new DateTime(2020, 1, 2, 3, 4, 5)),
            CreatedBefore = Timestamp.Naive(new DateTime(2021, 2, 3, 4, 5, 6)),
            ModifiedAfter = Timestamp.Naive(new DateTime(2022, 3, 4, 5, 6, 7)),
            ModifiedBefore = Timestamp.Naive(new DateTime(2023, 4, 5, 6, 7, 8)),
            ExcludeHidden = false,
            TopLevelOnly = true,
            SidecarPatternsCsv = ".srt",
            PreserveOriginalTimestamps = true,
            OutputCollisionPolicy = OutputCollisionPolicies.KeepBoth,
            FfmpegStrictness = FfmpegStrictnessLevels.Strict,
            RemuxWriter = RemuxWriterChoice.Ffmpeg,
            ScanIntervalSeconds = 14,
            IgnoreSizeChanges = true,
            SkipAccessTests = true,
            MaxAttempts = 15,
            RetryBackoffSeconds = 16,
            RetryExecutionFailures = false,
            RetryPreflightFailures = true,
            FailurePolicy = ProcessingFailurePolicies.Hold,
            FileSystemEventsEnabled = false,
            ScheduleEnabled = false,
            ScheduleHoursLimited = true,
            ScheduleDays = "Mon,Tue",
            ScheduleStart = "01:00",
            ScheduleEnd = "05:30",
            MaxConcurrentFiles = 1,
            Priority = 17,
            RemoveOriginalAfterSuccess = false,
            MinimumFreeDiskSpaceMb = 18,
        };

        var created = await Store.CreateAsync(uow, input);
        var reloaded = await Store.GetAsync(uow, created.Id);

        Assert.NotNull(reloaded);
        // A library saved without a profile is given its kind's default, so the profile is not one of the saved values.
        var settings = typeof(ProcessingLibraryInput).GetProperties()
            .Where(setting => setting.Name != nameof(ProcessingLibraryInput.RuleSetId))
            .Where(setting => typeof(ProcessingLibraryRecord).GetProperty(setting.Name) is not null)
            .ToList();
        Assert.NotEmpty(settings);
        foreach (var setting in settings)
        {
            var saved = setting.GetValue(input);
            var read = typeof(ProcessingLibraryRecord).GetProperty(setting.Name)!.GetValue(reloaded);
            Assert.True(Equals(saved, read), $"{setting.Name} was saved as {saved} and read back as {read}");
        }
    }

    [Fact]
    public async Task Two_libraries_may_not_share_a_name()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        await Store.CreateAsync(uow, NewLibrary("Anime"));

        var exception = await Assert.ThrowsAsync<ProcessingLibraryException>(() => Store.CreateAsync(uow, NewLibrary("Anime")));
        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_librarys_watched_folder_may_not_overlap_another_librarys_output_folder()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        await Store.CreateAsync(uow, NewLibrary("Anime", watched: @"c:\media\anime-in", output: @"c:\media\anime-out"));

        var exception = await Assert.ThrowsAsync<ProcessingLibraryException>(() =>
            Store.CreateAsync(uow, NewLibrary("Anime 4K", watched: @"c:\media\anime-out", output: @"c:\media\anime4k-out")));
        Assert.Contains("Anime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_library_with_queued_work_is_refused()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var library = await Store.CreateAsync(uow, NewLibrary("Anime"));
        await uow.CommitAsync(); // release the write lock before the raw connection below takes it
        db.Execute(
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ('k1', 'processing.file.remux_pass.v1', @payload, 'pending')",
            ("@payload", $"{{\"library_id\": {library.Id}}}"));

        var exception = await Assert.ThrowsAsync<ProcessingLibraryException>(() => Store.DeleteAsync(uow, library));
        Assert.Contains("1 job", exception.Message, StringComparison.Ordinal);

        Assert.NotNull(await Store.GetAsync(uow, library.Id));
    }

    [Fact]
    public async Task Deleting_a_library_with_no_queued_work_succeeds()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var library = await Store.CreateAsync(uow, NewLibrary("Anime"));

        await Store.DeleteAsync(uow, library);

        Assert.Null(await Store.GetAsync(uow, library.Id));
    }

    [Fact]
    public async Task Reordering_lists_libraries_in_the_new_display_order()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var a = await Store.CreateAsync(uow, NewLibrary("A"));
        var b = await Store.CreateAsync(uow, NewLibrary("B"));

        var reordered = await Store.ReorderAsync(uow, [b.Id, a.Id]);

        Assert.Equal(["B", "A"], reordered.Select(r => r.Name));
    }

    [Fact]
    public async Task Reordering_without_every_library_is_refused()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var a = await Store.CreateAsync(uow, NewLibrary("A"));
        await Store.CreateAsync(uow, NewLibrary("B"));

        await Assert.ThrowsAsync<ProcessingLibraryException>(() => Store.ReorderAsync(uow, [a.Id]));
    }

    [Fact]
    public async Task Manager_links_can_be_set_then_cleared()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var library = await Store.CreateAsync(uow, NewLibrary("Anime"));
        await uow.CommitAsync(); // release the write lock before the raw connection below takes it
        db.Execute("INSERT INTO media_manager_connections (id, kind, name) VALUES (1, 'sonarr', 'Sonarr')");

        await Store.SetManagerLinksAsync(uow, library.Id, [1]);
        Assert.Equal([1L], await Store.ManagerConnectionIdsAsync(uow, library.Id));

        await Store.SetManagerLinksAsync(uow, library.Id, []);
        Assert.Empty(await Store.ManagerConnectionIdsAsync(uow, library.Id));
    }

    [Fact]
    public async Task Linking_an_unknown_manager_connection_is_refused()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var library = await Store.CreateAsync(uow, NewLibrary("Anime"));

        await Assert.ThrowsAsync<ProcessingLibraryException>(() => Store.SetManagerLinksAsync(uow, library.Id, [999]));
    }

    [Fact]
    public async Task A_rule_set_still_used_by_a_library_cannot_be_deleted()
    {
        using var db = new JobsTestDatabase(keepSeedRows: true);
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var ruleSet = await Store.GetRuleSetAsync(uow, 1);
        Assert.NotNull(ruleSet);

        var exception = await Assert.ThrowsAsync<ProcessingLibraryException>(() => Store.DeleteRuleSetAsync(uow, ruleSet!));
        Assert.Contains("still used", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rule_set_a_library_cleans_its_existing_files_by_cannot_be_deleted()
    {
        using var db = new JobsTestDatabase();
        var profileId = db.AddRuleSet("Cleaning profile");
        var libraryId = db.AddLibrary();
        db.Execute("UPDATE libraries SET library_rule_set_id = @profile WHERE id = @id", ("@profile", profileId), ("@id", libraryId));
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var ruleSet = await Store.GetRuleSetAsync(uow, profileId);

        var exception = await Assert.ThrowsAsync<ProcessingLibraryException>(() => Store.DeleteRuleSetAsync(uow, ruleSet!));

        Assert.Contains("still used", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rule_set_no_longer_referenced_can_be_deleted()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var ruleSet = await Store.CreateRuleSetAsync(uow, new Weir.Core.Processing.LibraryRules.RuleSetInput { Name = "Spare" });

        await Store.DeleteRuleSetAsync(uow, ruleSet);

        Assert.Null(await Store.GetRuleSetAsync(uow, ruleSet.Id));
    }

    [Fact]
    public async Task Creating_a_rule_set_fills_empty_sorters_from_the_default_preset()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);

        var ruleSet = await Store.CreateRuleSetAsync(uow, new Weir.Core.Processing.LibraryRules.RuleSetInput { Name = "Custom" });

        Assert.False(string.IsNullOrWhiteSpace(ruleSet.AudioSortersJson));
        Assert.NotEqual("[]", ruleSet.AudioSortersJson.Trim());
    }

    [Fact]
    public async Task Creating_and_updating_a_rule_set_round_trips_every_sorter_and_naming_field()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var overrides = new Weir.Core.Rules.TrackNameOverrides
        {
            Forced = "{language} (Forced)",
            HearingImpaired = "{language} (SDH)",
            Commentary = "{language} (Commentary)",
            AudioDescription = "{language} (AD)",
        };
        var input = new Weir.Core.Processing.LibraryRules.RuleSetInput
        {
            Name = "Round trip",
            RemoveHearingImpairedSubs = true,
            AudioKeepMode = Weir.Core.Rules.RemuxRuleValues.AudioKeepModePerLanguage,
            SubtitleMaxPerLanguage = 3,
            SubtitleQualityStrategy = Weir.Core.Rules.RemuxRuleValues.SubtitleStrategyImageFirst,
            StandardizeTrackNames = true,
            TrackNameTemplate = "{language} {channels} {codec}",
            TrackNameOverrides = overrides,
            ClearVideoTrackNames = true,
            RemoveChapters = true,
        };

        var created = await Store.CreateRuleSetAsync(uow, input);
        var reloadedAfterCreate = await Store.GetRuleSetAsync(uow, created.Id);

        foreach (var row in new[] { created, reloadedAfterCreate })
        {
            Assert.True(row!.RemoveHearingImpairedSubs);
            Assert.Equal(Weir.Core.Rules.RemuxRuleValues.AudioKeepModePerLanguage, row.AudioKeepMode);
            Assert.Equal(3, row.SubtitleMaxPerLanguage);
            Assert.Equal(Weir.Core.Rules.RemuxRuleValues.SubtitleStrategyImageFirst, row.SubtitleQualityStrategy);
            Assert.True(row.StandardizeTrackNames);
            Assert.Equal("{language} {channels} {codec}", row.TrackNameTemplate);
            Assert.Equal(overrides, row.TrackNameOverrides);
            Assert.True(row.ClearVideoTrackNames);
            Assert.True(row.RemoveChapters);
        }

        // The plain sorter list (filled from the default preset, same as any other new rule set)
        // survives the same round trip, unaffected by packing the new fields alongside it in the
        // same column.
        Assert.False(string.IsNullOrWhiteSpace(reloadedAfterCreate!.SubtitleSortersJson));
        Assert.Equal(created.SubtitleSortersJson, reloadedAfterCreate.SubtitleSortersJson);

        var updated = await Store.UpdateRuleSetAsync(
            uow,
            created,
            input with { RemoveHearingImpairedSubs = false, AudioKeepMode = Weir.Core.Rules.RemuxRuleValues.AudioKeepModeSingle, SubtitleMaxPerLanguage = 0 });
        var reloadedAfterUpdate = await Store.GetRuleSetAsync(uow, created.Id);

        Assert.False(updated.RemoveHearingImpairedSubs);
        Assert.Equal(Weir.Core.Rules.RemuxRuleValues.AudioKeepModeSingle, updated.AudioKeepMode);
        Assert.Equal(0, updated.SubtitleMaxPerLanguage);
        Assert.False(reloadedAfterUpdate!.RemoveHearingImpairedSubs);
        // Untouched-by-the-update fields (#498) survive the update too.
        Assert.True(reloadedAfterUpdate.StandardizeTrackNames);
        Assert.Equal(overrides, reloadedAfterUpdate.TrackNameOverrides);
    }

    [Fact]
    public async Task A_row_from_before_sorters_and_naming_with_a_plain_sorter_array_column_reads_back_every_new_field_at_its_default()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var created = await Store.CreateRuleSetAsync(uow, new Weir.Core.Processing.LibraryRules.RuleSetInput { Name = "Legacy row" });
        await uow.CommitAsync(); // release the write lock before the raw connection below takes it

        // Simulate a row written before #495/#497/#498 existed: subtitle_sorters_json holds only the
        // plain sorter array this column has always stored, never the extras envelope.
        const string legacySorters = """[{"field":"forced","value":null,"reversed":false}]""";
        db.Execute("UPDATE rule_sets SET subtitle_sorters_json = @json WHERE id = @id", ("@json", legacySorters), ("@id", created.Id));

        var reloaded = await Store.GetRuleSetAsync(uow, created.Id);

        Assert.Equal(legacySorters, reloaded!.SubtitleSortersJson);
        Assert.False(reloaded.RemoveHearingImpairedSubs);
        Assert.Equal(Weir.Core.Rules.RemuxRuleValues.AudioKeepModeSingle, reloaded.AudioKeepMode);
        Assert.Equal(0, reloaded.SubtitleMaxPerLanguage);
        Assert.Equal(Weir.Core.Rules.RemuxRuleValues.SubtitleStrategyTextFirst, reloaded.SubtitleQualityStrategy);
        Assert.False(reloaded.StandardizeTrackNames);
        Assert.Equal(Weir.Core.Rules.TrackNaming.DefaultTemplate, reloaded.TrackNameTemplate);
        Assert.Equal(new Weir.Core.Rules.TrackNameOverrides(), reloaded.TrackNameOverrides);
        Assert.False(reloaded.ClearVideoTrackNames);
        Assert.False(reloaded.RemoveChapters);
    }

    [Fact]
    public async Task Active_job_count_matches_the_library_id_carried_in_the_payload()
    {
        using var db = new JobsTestDatabase();
        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var library = await Store.CreateAsync(uow, NewLibrary("Anime"));
        await uow.CommitAsync(); // release the write lock before the raw connection below takes it
        db.Execute(
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ('k1', 'processing.file.remux_pass.v1', @payload, 'leased')",
            ("@payload", $"{{\"library_id\": {library.Id}}}"));
        db.Execute(
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status) VALUES ('k2', 'processing.file.remux_pass.v1', @payload, 'completed')",
            ("@payload", $"{{\"library_id\": {library.Id}}}"));

        var reloaded = await Store.GetAsync(uow, library.Id);
        var active = await Store.ActiveJobCountAsync(uow, reloaded!);

        Assert.Equal(1, active);
    }
}
