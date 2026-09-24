using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>
/// SQLite access for Processing libraries and rule sets, plus the row IO <c>LibraryDiscoveryService</c> (#554)
/// needs for a discovered library's create and unlink. The reject-support gate lives in <c>RejectSupportEvaluator</c>.
/// Rule-set CRUD and its default-profile bookkeeping live in the <c>.RuleSets</c> partial.
/// </summary>
public static partial class LibraryStore
{
    private const string LibraryColumns =
        "id, name, enabled, media_type, display_order, watched_folder, work_folder, output_folder, " +
        "media_extensions_csv, exclude_markers_csv, include_patterns_csv, exclude_patterns_csv, min_file_size_mb, max_file_size_mb, " +
        "rejected_file_action, min_file_age_seconds, created_after, created_before, modified_after, modified_before, " +
        "exclude_hidden, top_level_only, sidecar_patterns_csv, preserve_original_timestamps, output_collision_policy, " +
        "hardware_decode_mode, hardware_device, hardware_disabled_vendors_csv, ffmpeg_strictness, scan_interval_seconds, " +
        "hold_minutes, file_detection_interval_seconds, ignore_size_changes, file_system_events_enabled, skip_access_tests, " +
        "schedule_enabled, schedule_hours_limited, schedule_days, schedule_grid, schedule_start, schedule_end, max_attempts, " +
        "retry_backoff_seconds, retry_execution_failures, retry_preflight_failures, failure_policy, max_concurrent_files, " +
        "priority, rule_set_id, discovered_from_connection_id, discovered_library_key, created_at, updated_at, " +
        // #548: appended, not inserted - ReadLibrary reads by position.
        "remux_writer, rewrite_with_ffmpeg, remove_original_after_success";

    public static async Task<List<ProcessingLibraryRecord>> ListAsync(UnitOfWork uow, bool enabledOnly = false)
    {
        var sql = $"SELECT {LibraryColumns} FROM libraries" + (enabledOnly ? " WHERE enabled = 1" : string.Empty) +
                  " ORDER BY display_order, id";
        return await uow.QueryAsync(sql, ReadLibrary).ConfigureAwait(false);
    }

    public static Task<ProcessingLibraryRecord?> GetAsync(UnitOfWork uow, long id) =>
        uow.QuerySingleAsync($"SELECT {LibraryColumns} FROM libraries WHERE id = @id", ReadLibrary, ("@id", id));

    public static Task<ProcessingLibraryRecord?> GetByNameAsync(UnitOfWork uow, string name) =>
        uow.QuerySingleAsync($"SELECT {LibraryColumns} FROM libraries WHERE name = @name", ReadLibrary, ("@name", name));

    /// <summary>The first library covering a scope, in display order.</summary>
    public static Task<ProcessingLibraryRecord?> SeededForScopeAsync(UnitOfWork uow, string mediaScope) =>
        uow.QuerySingleAsync(
            $"SELECT {LibraryColumns} FROM libraries WHERE media_type = @scope ORDER BY display_order, id LIMIT 1",
            ReadLibrary, ("@scope", ProcessingMediaScopes.Normalize(mediaScope)));

    public static async Task<List<long>> ManagerConnectionIdsAsync(UnitOfWork uow, long libraryId)
    {
        var rows = await uow.QueryAsync(
            "SELECT connection_id FROM library_manager_links WHERE library_id = @id ORDER BY connection_id",
            reader => reader.GetInt64(0), ("@id", libraryId)).ConfigureAwait(false);
        return rows;
    }

    /// <summary>The libraries linked to one connection, in <c>display_order</c> then id (the reverse of <see cref="ManagerConnectionIdsAsync"/>).</summary>
    public static async Task<List<ProcessingLibraryRecord>> LibrariesForConnectionIdAsync(UnitOfWork uow, long connectionId)
    {
        var ids = await uow.QueryAsync(
            "SELECT library_id FROM library_manager_links WHERE connection_id = @id",
            reader => reader.GetInt64(0), ("@id", connectionId)).ConfigureAwait(false);
        if (ids.Count == 0)
        {
            return [];
        }

        var wanted = ids.ToHashSet();
        return [.. (await ListAsync(uow).ConfigureAwait(false)).Where(library => wanted.Contains(library.Id))];
    }

    public static async Task<HashSet<long>> KnownConnectionIdsAsync(UnitOfWork uow, IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var placeholders = string.Join(",", ids.Select((_, i) => $"@id{i}"));
        var parameters = ids.Select((id, i) => ($"@id{i}", (object?)id)).ToArray();
        var rows = await uow.QueryAsync(
            $"SELECT id FROM media_manager_connections WHERE id IN ({placeholders})",
            reader => reader.GetInt64(0), parameters).ConfigureAwait(false);
        return [.. rows];
    }

    /// <summary>How many queued or leased Processing jobs belong to this library.</summary>
    public static async Task<int> ActiveJobCountAsync(UnitOfWork uow, ProcessingLibraryRecord library)
    {
        var libraries = await ListAsync(uow).ConfigureAwait(false);
        var seededForScope = libraries.FirstOrDefault(row => row.MediaType == library.MediaType);
        var isSeeded = seededForScope is not null && seededForScope.Id == library.Id;

        var payloads = await uow.QueryAsync(
            "SELECT payload_json FROM jobs WHERE status IN ('pending', 'leased')",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0)).ConfigureAwait(false);

        var count = 0;
        foreach (var raw in payloads)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            WireValue data;
            try
            {
                data = WireJsonParser.Parse(raw);
            }
            catch (WireJsonDecodeException)
            {
                continue;
            }

            if (data is not WireObject dict)
            {
                continue;
            }

            if (dict.TryGetValue("library_id", out var libraryIdValue) && libraryIdValue is WireInteger libraryIdInt)
            {
                if (libraryIdInt.Value == library.Id)
                {
                    count++;
                }

                continue;
            }

            if (isSeeded)
            {
                var scope = dict.TryGetValue("media_scope", out var scopeValue) && scopeValue is WireString scopeStr
                    ? ProcessingMediaScopes.Normalize(scopeStr.Value)
                    : ProcessingMediaScopes.Movie;
                if (scope == library.MediaType)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static async Task<ProcessingLibraryRecord> CreateAsync(UnitOfWork uow, ProcessingLibraryInput body, string? weirHome = null)
    {
        var existingNames = (await ListAsync(uow).ConfigureAwait(false)).Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
        var name = LibraryRules.ValidateName(body.Name, existingNames);
        var scope = LibraryRules.ValidateScope(body.MediaType);
        var highestOrder = await uow.ScalarAsync("SELECT MAX(display_order) FROM libraries").ConfigureAwait(false);
        var displayOrder = (highestOrder is null or DBNull ? 0 : Convert.ToInt64(highestOrder, System.Globalization.CultureInfo.InvariantCulture)) + 1;

        var ruleSetExists = body.RuleSetId is { } wantedRuleSet && await GetRuleSetAsync(uow, wantedRuleSet).ConfigureAwait(false) is not null;
        var others = await OtherFoldersAsync(uow, excludeId: null).ConfigureAwait(false);
        var row = LibraryRules.ApplyFields(new ProcessingLibraryRecord { Name = name, MediaType = scope, DisplayOrder = displayOrder }, body, ruleSetExists);
        LibraryRules.ValidateFolders(row.WatchedFolder, row.WorkFolder, row.OutputFolder, others, weirHome);

        // A library made without a profile gets its kind's default, so no library runs on rules nobody can see.
        if (row.RuleSetId is null)
        {
            row = row with { RuleSetId = await DefaultProfileIdAsync(uow, scope).ConfigureAwait(false) };
        }

        await InsertAsync(uow, row).ConfigureAwait(false);
        var created = await GetByNameAsync(uow, name).ConfigureAwait(false) ?? throw new InvalidOperationException("Library insert race.");
        await SetManagerLinksAsync(uow, created.Id, body.ManagerConnectionIds).ConfigureAwait(false);
        return created;
    }

    public static async Task<ProcessingLibraryRecord> UpdateAsync(UnitOfWork uow, ProcessingLibraryRecord existing, ProcessingLibraryInput body, string? weirHome = null)
    {
        var existingNames = (await ListAsync(uow).ConfigureAwait(false))
            .Where(l => l.Id != existing.Id).Select(l => l.Name).ToHashSet(StringComparer.Ordinal);
        var name = LibraryRules.ValidateName(body.Name, existingNames);
        var scope = LibraryRules.ValidateScope(body.MediaType);
        var ruleSetExists = body.RuleSetId is { } wantedRuleSet && await GetRuleSetAsync(uow, wantedRuleSet).ConfigureAwait(false) is not null;
        var others = await OtherFoldersAsync(uow, excludeId: existing.Id).ConfigureAwait(false);
        var row = LibraryRules.ApplyFields(existing with { Name = name, MediaType = scope }, body, ruleSetExists);
        LibraryRules.ValidateFolders(row.WatchedFolder, row.WorkFolder, row.OutputFolder, others, weirHome);

        await UpdateRowAsync(uow, row).ConfigureAwait(false);
        var updated = await GetAsync(uow, existing.Id).ConfigureAwait(false) ?? throw new InvalidOperationException("Library disappeared during update.");
        await SetManagerLinksAsync(uow, updated.Id, body.ManagerConnectionIds).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Creates a library imported from a media manager and links it to that manager as it is created, so Weir can
    /// reject a bad download through it (#651).
    /// </summary>
    /// <remarks>
    /// The row is built from the manager's own descriptor rather than an operator's request body, so
    /// <c>LibraryRules.ApplyFields</c>/<c>ValidateFolders</c> (folder-overlap and full-field validation) does not run.
    /// Columns the descriptor does not set keep their schema defaults, mirrored by
    /// <see cref="ProcessingLibraryRecord"/>'s own property defaults.
    /// </remarks>
    public static async Task<ProcessingLibraryRecord> CreateDiscoveredAsync(UnitOfWork uow, ProcessingLibraryRecord row)
    {
        if (row.RuleSetId is null)
        {
            row = row with { RuleSetId = await DefaultProfileIdAsync(uow, row.MediaType).ConfigureAwait(false) };
        }

        await InsertAsync(uow, row).ConfigureAwait(false);
        var created = await GetByNameAsync(uow, row.Name).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Discovered library insert race.");
        if (row.DiscoveredFromConnectionId is { } connectionId)
        {
            await SetManagerLinksAsync(uow, created.Id, [connectionId]).ConfigureAwait(false);
        }

        return created;
    }

    /// <summary>Forgets where a library came from, keeping the library itself untouched.</summary>
    public static async Task<ProcessingLibraryRecord> UnlinkAsync(UnitOfWork uow, ProcessingLibraryRecord row)
    {
        await uow.ExecuteAsync(
            "UPDATE libraries SET discovered_from_connection_id = NULL, discovered_library_key = NULL, " +
            "updated_at = CURRENT_TIMESTAMP WHERE id = @id",
            ("@id", row.Id)).ConfigureAwait(false);
        return await GetAsync(uow, row.Id).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Library disappeared during unlink.");
    }

    public static async Task DeleteAsync(UnitOfWork uow, ProcessingLibraryRecord row)
    {
        var active = await ActiveJobCountAsync(uow, row).ConfigureAwait(false);
        if (active > 0)
        {
            throw new ProcessingLibraryException(
                $"{row.Name} still has {active} job{(active == 1 ? string.Empty : "s")} queued or running. " +
                "Wait for them to finish, or cancel them, before removing the library — they resolve their folders from it.");
        }

        await uow.ExecuteAsync("DELETE FROM libraries WHERE id = @id", ("@id", row.Id)).ConfigureAwait(false);
    }

    public static async Task<List<ProcessingLibraryRecord>> ReorderAsync(UnitOfWork uow, IReadOnlyList<long> orderedIds)
    {
        var rows = (await ListAsync(uow).ConfigureAwait(false)).ToDictionary(r => r.Id);
        var unknown = orderedIds.Where(id => !rows.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ProcessingLibraryException($"No library with id {unknown[0]}.");
        }

        if (orderedIds.Distinct().Count() != rows.Count)
        {
            throw new ProcessingLibraryException("Reordering must list every library exactly once.");
        }

        for (var position = 0; position < orderedIds.Count; position++)
        {
            await uow.ExecuteAsync(
                "UPDATE libraries SET display_order = @order WHERE id = @id",
                ("@order", (long)position), ("@id", orderedIds[position])).ConfigureAwait(false);
        }

        return await ListAsync(uow).ConfigureAwait(false);
    }

    public static async Task SetManagerLinksAsync(UnitOfWork uow, long libraryId, IReadOnlyList<long> connectionIds)
    {
        var known = await KnownConnectionIdsAsync(uow, connectionIds).ConfigureAwait(false);
        var wanted = LibraryRules.ValidateManagerConnections(connectionIds, known);
        var existing = (await ManagerConnectionIdsAsync(uow, libraryId).ConfigureAwait(false)).ToHashSet();
        foreach (var stale in existing.Except(wanted))
        {
            await uow.ExecuteAsync(
                "DELETE FROM library_manager_links WHERE library_id = @lib AND connection_id = @conn",
                ("@lib", libraryId), ("@conn", stale)).ConfigureAwait(false);
        }

        foreach (var added in wanted.Except(existing))
        {
            await uow.ExecuteAsync(
                "INSERT INTO library_manager_links (library_id, connection_id) VALUES (@lib, @conn)",
                ("@lib", libraryId), ("@conn", added)).ConfigureAwait(false);
        }
    }

    private static async Task<List<OtherLibraryFolders>> OtherFoldersAsync(UnitOfWork uow, long? excludeId)
    {
        var rows = await ListAsync(uow).ConfigureAwait(false);
        return [.. rows.Where(r => r.Id != excludeId).Select(r => new OtherLibraryFolders(r.Id, r.Name, r.WatchedFolder, r.OutputFolder))];
    }
}
