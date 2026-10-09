using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Runtime;

/// <summary>A copy of Weir's data taken before an update changed it, and when it was taken.</summary>
/// <param name="DatabasePath">The copy of the database.</param>
/// <param name="TakenAt">When the copy was taken, from its file name.</param>
public sealed record PreUpdateBackupFile(string DatabasePath, DateTimeOffset TakenAt);

/// <summary>Weir could not save a copy of its data before updating, so it changed nothing. The message is for the operator.</summary>
public sealed class PreUpdateBackupException : Exception
{
    public PreUpdateBackupException()
    {
    }

    public PreUpdateBackupException(string message)
        : base(message)
    {
    }

    public PreUpdateBackupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The copy of Weir's data taken when a start is about to update the database: a consistent copy of the database
/// (<c>VACUUM INTO</c>, never a file copy of a database with a write-ahead log) and of the settings files in the data folder,
/// kept in <c>{backup_dir}/pre-update</c> under one name:
/// <c>weir-{old revision number}-to-{new version}-{UTC time}.db</c>, with each settings file next to it as
/// <c>weir-…-{UTC time}.{file name}</c>. The newest <see cref="MaxKept"/> copies stay; older ones go, and nothing else in the
/// folder is ever touched.
/// </summary>
/// <remarks>
/// The copy holds sign-in sessions and saved connection secrets, so the folder and its files are owner-only where the platform
/// allows it. On Windows they inherit the data folder's owner-only access list, as the database does.
/// </remarks>
public sealed class PreUpdateBackup
{
    public const int MaxKept = 5;
    public const string FolderName = "pre-update";

    private const string Prefix = "weir-";
    private const string DatabaseSuffix = ".db";
    private const string UnfinishedSuffix = ".db.partial";
    private const string TimeFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>The length of a time written in <see cref="TimeFormat"/>.</summary>
    private const int TimeLength = 16;

    private static readonly string[] SettingsFiles =
    [
        LanAccessFile.FileName,
        UpdateFiles.SettingsFileName,
        DirectPlayEvaluation.OverrideFileName,
    ];

    private static readonly UnixFileMode OwnerOnlyFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly UnixFileMode OwnerOnlyFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _backupDir;
    private readonly string _home;
    private readonly string _version;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    /// <param name="backupDir">The backup folder (<c>WEIR_BACKUP_DIR</c>); copies go in its <see cref="FolderName"/> folder.</param>
    /// <param name="home">Weir's data folder, where the settings files are.</param>
    /// <param name="version">The version that is about to update the database.</param>
    /// <param name="time">Names the copy.</param>
    /// <param name="logger">Where the copy is announced.</param>
    public PreUpdateBackup(string backupDir, string home, string version, TimeProvider time, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _backupDir = backupDir;
        _home = home;
        _version = FileNamePart(version);
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Where the copies are kept for <paramref name="backupDir"/>.</summary>
    public static string FolderIn(string backupDir) => Path.Join(backupDir, FolderName);

    /// <summary>The newest copy in <paramref name="backupDir"/>, or null when none has been taken.</summary>
    public static PreUpdateBackupFile? Latest(string backupDir)
    {
        var folder = FolderIn(backupDir);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(folder)
                .Select(path => (Path: path, Part: Classify(Path.GetFileName(path))))
                .Where(file => file.Part is { Kind: PartKind.Database })
                .OrderByDescending(file => file.Part!.TakenAt)
                .ThenByDescending(file => file.Part!.Stem, StringComparer.Ordinal)
                .Select(file => new PreUpdateBackupFile(file.Path, file.Part!.TakenAt))
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Copies the database behind <paramref name="connection"/> and the settings files, checks the copy of the database opens at
    /// <paramref name="fromRevision"/>, then prunes older copies. Throws <see cref="PreUpdateBackupException"/> when any step
    /// fails, after removing what it had written.
    /// </summary>
    public void Take(SqliteConnection connection, string fromRevision)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrEmpty(fromRevision);
        var folder = FolderIn(_backupDir);
        var takenAt = _time.GetUtcNow();
        var stem = $"{Prefix}{RevisionNumber(fromRevision)}-to-{_version}-{takenAt.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture)}";
        var written = new List<string>();
        try
        {
            CreateFolder(folder);
            var database = Path.Join(folder, stem + DatabaseSuffix);
            var unfinished = Path.Join(folder, stem + UnfinishedSuffix);
            written.Add(unfinished);
            CopyDatabase(connection, unfinished);
            VerifyDatabase(unfinished, fromRevision);
            File.Move(unfinished, database);
            written.Remove(unfinished);
            written.Add(database);
            RestrictFile(database);
            foreach (var name in SettingsFiles)
            {
                var source = Path.Join(_home, name);
                if (File.Exists(source))
                {
                    var copy = Path.Join(folder, $"{stem}.{name}");
                    if (File.Exists(copy))
                    {
                        throw new IOException($"{copy} already exists");
                    }

                    written.Add(copy);
                    File.Copy(source, copy);
                    RestrictFile(copy);
                }
            }
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            Remove(written);
            throw new PreUpdateBackupException(
                $"Weir couldn't save a copy of its data before updating, so it didn't change anything: {exception.Message}",
                exception);
        }

        _logger.LogInformation(
            "Before updating, Weir saved a copy of its data from={From} to={To} path={Path}",
            fromRevision,
            _version,
            Path.Join(folder, stem + DatabaseSuffix));
        Prune(folder);
    }

    /// <summary>Removes every copy past the newest <see cref="MaxKept"/>, and any copy a crash left unfinished.</summary>
    private void Prune(string folder)
    {
        try
        {
            var parts = Directory.EnumerateFiles(folder)
                .Select(path => (Path: path, Part: Classify(Path.GetFileName(path))))
                .Where(file => file.Part is not null)
                .ToList();
            var kept = parts
                .Where(file => file.Part!.Kind == PartKind.Database)
                .OrderByDescending(file => file.Part!.TakenAt)
                .ThenByDescending(file => file.Part!.Stem, StringComparer.Ordinal)
                .Take(MaxKept)
                .Select(file => file.Part!.Stem)
                .ToHashSet(StringComparer.Ordinal);
            Remove(parts.Where(file => file.Part!.Kind == PartKind.Unfinished || !kept.Contains(file.Part.Stem)).Select(file => file.Path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Weir could not remove older copies of its data from {Folder}", folder);
        }
    }

    private static void CopyDatabase(SqliteConnection connection, string path)
    {
        // VACUUM INTO refuses to write over an existing file, and a crash can leave one from an earlier try.
        File.Delete(path);
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path";
        command.Parameters.AddWithValue("$path", path);
        command.ExecuteNonQuery();
    }

    /// <summary>Opens the copy on its own connection, checks it is whole and that it records <paramref name="revision"/>.</summary>
    private static void VerifyDatabase(string path, string revision)
    {
        using var copy = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        copy.Open();
        using var check = copy.CreateCommand();
        check.CommandText = "PRAGMA quick_check";
        var verdict = Convert.ToString(check.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (verdict != "ok")
        {
            throw new IOException($"the copy of the database did not pass its integrity check ({verdict})");
        }

        var recorded = SchemaMigrator.ReadRecordedRevision(copy);
        if (recorded != revision)
        {
            throw new IOException($"the copy of the database records revision '{recorded}', not '{revision}'");
        }
    }

    private static void CreateFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }

        Directory.CreateDirectory(folder, OwnerOnlyFolder);
        File.SetUnixFileMode(folder, OwnerOnlyFolder);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, OwnerOnlyFile);
        }
    }

    private static void Remove(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file that cannot be removed now is found again by the next prune.
            }
        }
    }

    /// <summary>The leading number of a revision: <c>0076</c> for <c>0076_handoff_skipped_extras</c>.</summary>
    private static string RevisionNumber(string revision)
    {
        var end = revision.IndexOf('_', StringComparison.Ordinal);
        return FileNamePart(end < 0 ? revision : revision[..end]);
    }

    /// <summary>
    /// <paramref name="value"/> made safe for a file name. A version's build metadata (after <c>+</c>) is left out, as version
    /// comparisons leave it out.
    /// </summary>
    private static string FileNamePart(string value)
    {
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        var core = (plus < 0 ? value : value[..plus]).Trim();
        return string.Concat(core.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
    }

    private enum PartKind
    {
        Database,
        Settings,
        Unfinished,
    }

    /// <summary>One of this class's own files: the copy it belongs to (<paramref name="Stem"/>), when it was taken, and what it is.</summary>
    private sealed record Part(string Stem, DateTimeOffset TakenAt, PartKind Kind);

    /// <summary>What <paramref name="fileName"/> is, or null when it is not one of the files this class writes.</summary>
    private static Part? Classify(string fileName)
    {
        if (!fileName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = new List<(string Suffix, PartKind Kind)> { (DatabaseSuffix, PartKind.Database), (UnfinishedSuffix, PartKind.Unfinished) };
        parts.AddRange(SettingsFiles.Select(name => ($".{name}", PartKind.Settings)));
        foreach (var (suffix, kind) in parts)
        {
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var stem = fileName[..^suffix.Length];
            if (stem.Length > TimeLength + 1
                && stem.Contains("-to-", StringComparison.Ordinal)
                && DateTimeOffset.TryParseExact(
                    stem[^TimeLength..], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var takenAt))
            {
                return new Part(stem, takenAt, kind);
            }
        }

        return null;
    }
}
