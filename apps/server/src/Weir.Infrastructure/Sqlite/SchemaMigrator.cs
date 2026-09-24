using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Weir.Infrastructure.Sqlite;

/// <summary>One numbered SQL migration and the <c>alembic_version</c> value it leaves behind.</summary>
/// <param name="Number">Order of application.</param>
/// <param name="Revision">The revision recorded once the script has run.</param>
/// <param name="ResourceName">The embedded <c>Migrations/*.sql</c> file.</param>
public sealed record SchemaMigration(int Number, string Revision, string ResourceName);

/// <summary>Why the database could not be used.</summary>
public enum SchemaMismatchKind
{
    /// <summary>The file exists but no revision is recorded (including an empty file).</summary>
    Unversioned,

    /// <summary>A revision this build has never heard of (probably a newer release).</summary>
    UnknownRevision,

    /// <summary>A revision from before the baseline: an earlier Weir release must migrate it first.</summary>
    BehindHead,

    /// <summary>The version table is malformed.</summary>
    Incompatible,
}

/// <summary>The database cannot be opened by this build. The message is for the operator.</summary>
public sealed class DatabaseSchemaMismatchException : Exception
{
    public DatabaseSchemaMismatchException()
    {
    }

    public DatabaseSchemaMismatchException(string message)
        : base(message)
    {
    }

    public DatabaseSchemaMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DatabaseSchemaMismatchException(string message, SchemaMismatchKind kind)
        : base(message)
    {
        Kind = kind;
    }

    public SchemaMismatchKind Kind { get; }
}

/// <summary>What startup did to the database.</summary>
public enum SchemaStartupOutcome
{
    /// <summary>The file did not exist; the schema was created and seeded.</summary>
    Created,

    /// <summary>The database was already at this build's revision; nothing was written.</summary>
    AlreadyCurrent,

    /// <summary>
    /// The database was at an earlier revision in <see cref="SchemaMigrator.Migrations"/> (the baseline or a
    /// later migration): every migration after that revision, in order, was applied to bring it to head.
    /// </summary>
    Upgraded,
}

/// <summary>
/// Brings the database to this build's schema, or refuses to touch it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Version ledger:</b> every applied migration's revision is a row in the <c>alembic_version</c> table,
/// because every existing Weir database already carries that table; a separate version table would make
/// existing installs look unversioned. The schema is part of the contract (ADR-0017), and an install already
/// at head is adopted with no write at all. Each migration names the revision it leaves behind, continuing the
/// existing numbering (<c>0037_…</c>, #523).
/// </para>
/// <para>
/// <b>On startup:</b> a missing database file is created at head; a database whose recorded revisions already
/// cover every one of <see cref="Migrations"/> is adopted unchanged; one missing some is upgraded in place by
/// applying exactly those, in order, in one transaction (#557). A database that still holds only the single
/// row an older build always collapsed its revision to is read the same way: recording any migration implies
/// every earlier one (see <see cref="MigrationsToApply"/> for the one migration, #667, that is not implied by
/// a later one merged out of order with it). Anything else, including an existing file with no schema or a
/// revision from before the baseline, is refused with a message and no change. A pre-baseline database has to
/// be started once on an earlier Weir release, which migrates it to the baseline.
/// </para>
/// </remarks>
public sealed class SchemaMigrator
{
    public static readonly IReadOnlyList<SchemaMigration> Migrations =
    [
        new(1, "0036_drop_pruner_tables", "Weir.Infrastructure.Migrations.0001_baseline_0036_drop_pruner_tables.sql"),
        new(2, "0037_refiner_rule_set_extra_columns", "Weir.Infrastructure.Migrations.0002_refiner_rule_set_extra_columns.sql"),
        new(3, "0038_library_mode_settings", "Weir.Infrastructure.Migrations.0003_library_mode_settings.sql"),
        new(4, "0039_library_files", "Weir.Infrastructure.Migrations.0004_library_files.sql"),
        new(5, "0040_library_swaps", "Weir.Infrastructure.Migrations.0005_library_swaps.sql"),
        new(6, "0041_removed_tracks", "Weir.Infrastructure.Migrations.0006_removed_tracks.sql"),
        new(7, "0042_library_file_facets", "Weir.Infrastructure.Migrations.0007_library_file_facets.sql"),
        new(8, "0043_remux_writer", "Weir.Infrastructure.Migrations.0008_remux_writer.sql"),
        new(9, "0044_drop_the_refiner_name", "Weir.Infrastructure.Migrations.0009_drop_the_refiner_name.sql"),
        new(10, "0045_keep_original_download", "Weir.Infrastructure.Migrations.0010_keep_original_download.sql"),
        new(11, "0046_files_at_once", "Weir.Infrastructure.Migrations.0011_files_at_once.sql"),
        new(12, "0047_library_file_marks", "Weir.Infrastructure.Migrations.0012_library_file_marks.sql"),
        new(13, "0048_cleanup_intervals", "Weir.Infrastructure.Migrations.0013_cleanup_intervals.sql"),
        new(14, "0049_link_imported_libraries", "Weir.Infrastructure.Migrations.0014_link_imported_libraries.sql"),
        new(15, "0050_handback_outcomes", "Weir.Infrastructure.Migrations.0015_handback_outcomes.sql"),
        new(16, "0051_handoff_owning_connection", "Weir.Infrastructure.Migrations.0016_handoff_owning_connection.sql"),
        new(17, "0052_handoff_targets", "Weir.Infrastructure.Migrations.0017_handoff_targets.sql"),
        new(18, "0053_query_indexes", "Weir.Infrastructure.Migrations.0018_query_indexes.sql"),
    ];

    /// <summary>
    /// The schema before issue #557's migrations. The checked-in <c>schema/alembic-head.sql</c> reference and
    /// <c>SchemaParityTests</c> stay pinned to this revision (see apps/server/README.md, "Schema and migrations") rather than
    /// to the moving <see cref="HeadRevision"/>, so the reference file never changes.
    /// </summary>
    public static string BaselineRevision => Migrations[0].Revision;

    /// <summary>
    /// Revisions before the baseline, oldest first. A database at one of these was made by an earlier
    /// Weir release, which upgrades it to the baseline when started once.
    /// </summary>
    public static readonly IReadOnlyList<string> AlembicRevisionsBeforeBaseline =
    [
        "0001_weir_initial_schema",
        "0002_weir_schema_tip",
        "0003_pruner_auto_apply_snapshot_limits",
        "0004_pruner_uniqueness_constraints",
        "0005_refiner_guardrail_settings",
        "0006_trusted_device_sessions",
        "0007_indexes_and_retry_backoff",
        "0008_notification_channels",
        "0009_media_manager_connections",
        "0010_drop_subber_tables",
        "0011_refiner_libraries",
        "0012_refiner_file_states",
        "0013_refiner_file_settling",
        "0014_refiner_watcher",
        "0015_schedules_and_pause",
        "0016_runner_units",
        "0017_retry_and_sweeps",
        "0018_file_processing_log",
        "0019_track_sorters",
        "0020_metadata_rules",
        "0021_sidecar_migration",
        "0022_output_collision_policy",
        "0023_hardware_acceleration",
        "0024_metadata_provider",
        "0025_drop_refiner_singletons",
        "0026_session_client_labels",
        "0027_refiner_rejected_file_action",
        "0028_refiner_detection_windows",
        "0029_case_insensitive_usernames",
        "0030_refiner_file_media_facts",
        "0031_refiner_failure_policy",
        "0032_media_manager_handoffs",
        "0033_refiner_library_media_type",
        "0034_activity_history_facts",
        "0035_direct_play_facts",
    ];

    public static string HeadRevision => Migrations[^1].Revision;

    /// <summary>
    /// Migrations that must be confirmed by their own exact revision, never implied because some later-numbered
    /// migration is recorded. #667's <c>media_manager_handoff_targets</c> (17) and #734's <c>0053_query_indexes</c>
    /// (18) were developed in parallel and merged in the other order, so a database already recorded at 18 does
    /// not necessarily have 17: recording any other migration still safely implies every earlier one, because every
    /// one of those was merged one at a time onto an already-integrated main, but 17 is not implied by 18.
    /// </summary>
    private static readonly HashSet<int> NeverImpliedByALaterRevision = [17];

    private readonly SqliteDatabase _database;

    public SchemaMigrator(SqliteDatabase database)
    {
        _database = database;
    }

    public SchemaStartupOutcome EnsureAtHead()
    {
        // Only a missing file is a new install. A file that exists but holds no schema was made by
        // something else (or by a start that failed half-way), so it is refused as unversioned without
        // being opened (opening would switch it to WAL).
        var existed = File.Exists(_database.DatabasePath);
        if (existed && new FileInfo(_database.DatabasePath).Length == 0)
        {
            throw UnversionedError();
        }

        using var connection = _database.Open();
        var userObjects = ScalarLong(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'");
        if (userObjects == 0)
        {
            if (existed)
            {
                throw UnversionedError();
            }

            ApplyAll(connection);
            return SchemaStartupOutcome.Created;
        }

        var recorded = ReadRecordedRevisions(connection);
        var missing = MigrationsToApply(recorded);
        if (missing.Count == 0)
        {
            return SchemaStartupOutcome.AlreadyCurrent;
        }

        ApplyRange(connection, missing, Migrations.Select(m => m.Revision));
        return SchemaStartupOutcome.Upgraded;
    }

    /// <summary>
    /// The migrations, in order, still needed to reach head given what is recorded. Recording any migration whose
    /// <see cref="NeverImpliedByALaterRevision"/> does not list its own number also implies every earlier one
    /// (the classic, still-correct assumption for every migration merged the ordinary way): so a database recorded
    /// at, say, revision 5 needs 6 onward, and one recorded at 16 and 18 (18 does not imply 17) needs only 17.
    /// </summary>
    private static List<SchemaMigration> MigrationsToApply(List<string> recorded)
    {
        var matched = Migrations.Where(m => recorded.Contains(m.Revision)).ToList();
        if (matched.Count == 0)
        {
            if (recorded.Count == 1)
            {
                var single = recorded.Single();
                if (AlembicRevisionsBeforeBaseline.Contains(single, StringComparer.Ordinal))
                {
                    throw new DatabaseSchemaMismatchException(
                        $"Database revision {Quote(single)} was created by an older Weir release. " +
                        $"This build requires schema revision {Quote(HeadRevision)} and cannot upgrade older databases itself. " +
                        "Start the previous Weir release once so it migrates the database, then start this build again.",
                        SchemaMismatchKind.BehindHead);
                }

                throw new DatabaseSchemaMismatchException(
                    $"Database revision {Quote(single)} is not recognized by this Weir build " +
                    $"(expected head {Quote(HeadRevision)}). The database may come from a newer release; " +
                    "upgrade the application or restore a backup that matches this version.",
                    SchemaMismatchKind.UnknownRevision);
            }

            throw IncompatibleError(recorded);
        }

        if (recorded.Except(Migrations.Select(m => m.Revision)).Any())
        {
            throw IncompatibleError(recorded);
        }

        var confirmed = new HashSet<int>();
        foreach (var migration in matched)
        {
            if (!NeverImpliedByALaterRevision.Contains(migration.Number))
            {
                confirmed.UnionWith(Enumerable.Range(1, migration.Number).Where(number => !NeverImpliedByALaterRevision.Contains(number)));
            }

            confirmed.Add(migration.Number);
        }

        return [.. Migrations.Where(m => !confirmed.Contains(m.Number)).OrderBy(m => m.Number)];
    }

    private static DatabaseSchemaMismatchException IncompatibleError(IReadOnlyCollection<string> revisions) => new(
        $"Database records several schema revisions ({string.Join(", ", revisions.Select(Quote))}); " +
        $"this build requires exactly {Quote(HeadRevision)}. Restore a backup that matches this version.",
        SchemaMismatchKind.Incompatible);

    internal static string ReadMigrationSql(SchemaMigration migration)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(migration.ResourceName)
            ?? throw new InvalidOperationException($"Migration resource {migration.ResourceName} is missing from this build.");
        using var reader = new StreamReader(stream);
        // Line endings are normalized so sqlite_master holds the same schema text as existing databases on every platform.
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a fresh database at exactly <see cref="BaselineRevision"/>, ignoring every later migration.
    /// Test-only: <c>SchemaParityTests</c> uses this to compare against <c>alembic-head.sql</c> without that
    /// reference file ever changing.
    /// </summary>
    public SchemaStartupOutcome EnsureAtBaseline()
    {
        using var connection = _database.Open();
        ApplyRange(connection, Migrations.Take(1), [BaselineRevision]);
        return SchemaStartupOutcome.Created;
    }

    private static void ApplyAll(SqliteConnection connection) =>
        ApplyRange(connection, Migrations.OrderBy(m => m.Number), Migrations.Select(m => m.Revision));

    /// <summary>
    /// Runs <paramref name="migrations"/> in one transaction and records <paramref name="revisions"/> as every
    /// revision now applied, replacing whatever the table held before (<see cref="MigrationsToApply"/> decides
    /// which migrations that is; a full run to head records every one of <see cref="Migrations"/>, and
    /// <see cref="EnsureAtBaseline"/> keeps recording just the one it stops at).
    /// <para>
    /// Foreign keys are turned off <b>before</b> the transaction opens, which is SQLite's documented
    /// procedure for schema changes. <c>PRAGMA foreign_keys</c> is a no-op inside a transaction, and
    /// <c>defer_foreign_keys</c> only defers constraint <i>violations</i> to commit: neither stops
    /// <c>ON DELETE CASCADE</c>, which fires immediately, so a migration that rebuilds a table by dropping it
    /// would silently delete its children's rows, transitively (#578: dropping <c>refiner_files</c> would
    /// empty <c>library_files</c> and, through it, <c>library_file_facets</c>).
    /// </para>
    /// <para>
    /// A <c>foreign_key_check</c> runs before the commit so turning enforcement off cannot hide a migration
    /// that genuinely left the database inconsistent.
    /// </para>
    /// </summary>
    private static void ApplyRange(SqliteConnection connection, IEnumerable<SchemaMigration> migrations, IEnumerable<string> revisions)
    {
        Pragma(connection, "PRAGMA foreign_keys=off");
        try
        {
            ApplyRangeCore(connection, migrations, revisions);
        }
        finally
        {
            Pragma(connection, "PRAGMA foreign_keys=on");
        }
    }

    private static void Pragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ApplyRangeCore(SqliteConnection connection, IEnumerable<SchemaMigration> migrations, IEnumerable<string> revisions)
    {
        using var transaction = connection.BeginTransaction();

        foreach (var migration in migrations)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = ReadMigrationSql(migration);
            command.ExecuteNonQuery();
        }

        var revisionList = revisions.ToList();
        using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            var placeholders = revisionList.Select((_, index) => $"($r{index})");
            record.CommandText = $"DELETE FROM alembic_version; INSERT INTO alembic_version (version_num) VALUES {string.Join(", ", placeholders)};";
            for (var index = 0; index < revisionList.Count; index++)
            {
                record.Parameters.AddWithValue($"$r{index}", revisionList[index]);
            }

            record.ExecuteNonQuery();
        }

        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "PRAGMA foreign_key_check";
            using var violations = check.ExecuteReader();
            if (violations.Read())
            {
                throw new InvalidOperationException(
                    $"Migration left a foreign key violation in table '{violations.GetValue(0)}'.");
            }
        }

        transaction.Commit();
    }

    /// <summary>Every revision <c>alembic_version</c> currently records, in the order the table holds them.</summary>
    private static List<string> ReadRecordedRevisions(SqliteConnection connection)
    {
        var hasTable = ScalarLong(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'alembic_version'") > 0;
        var revisions = new List<string>();
        if (hasTable)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT version_num FROM alembic_version";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                revisions.Add(reader.GetString(0));
            }
        }

        return revisions.Count == 0 ? throw UnversionedError() : revisions;
    }

    /// <summary>The refusal for a database with no recorded revision, with the operator's options.</summary>
    private static DatabaseSchemaMismatchException UnversionedError() => new(
        "No Alembic revision is recorded for this database (migrations have not been applied). " +
        $"This build requires schema revision {Quote(HeadRevision)}. " +
        "Weir only opens a database that Weir created: point WEIR_DB_PATH at the right file, " +
        "restore a backup, or move this file aside so Weir creates a new one.",
        SchemaMismatchKind.Unversioned);

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A revision in single quotes, as the operator messages print it.</summary>
    private static string Quote(string value) => $"'{value}'";
}
