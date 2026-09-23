using Microsoft.Data.Sqlite;
using Weir.Core.Activity;
using Weir.Core.Jobs;
using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Rules;
using Weir.Infrastructure.IO;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>What a watched-folder walk found, and what it decided not to look at.</summary>
public sealed record WatchedFolderScanCandidates(
    IReadOnlyList<string> Files,
    int IgnoredUnsupportedType,
    IReadOnlyList<string> IgnoredUnsupportedExtensions);

/// <summary>
/// Filesystem scan helpers and duplicate guards for watched-folder remux scan dispatch: the disk-touching
/// half of the scan rules (#537), such as checking a candidate file exists and walking the watched folder tree.
/// </summary>
public static class WatchedFolderScanOps
{
    /// <summary>Files that legitimately sit beside media and are not a failed attempt at it.</summary>
    private static readonly HashSet<string> NonMediaCompanionSuffixes = new(StringComparer.Ordinal)
    {
        ".srt", ".sub", ".idx", ".ass", ".ssa", ".vtt", ".sup", ".nfo", ".txt", ".md", ".log", ".xml", ".json",
        ".yml", ".yaml", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".tbn", ".bmp", ".par2", ".sfv", ".nzb",
        ".torrent", ".url", ".db", ".ini", ".bak", ".mp3", ".flac", ".m4a", ".aac", ".ac3", ".dts", ".ogg", ".wav",
        ".part", ".partial", ".crdownload", ".downloading", ".tmp", ".!ut", ".!qb",
    };

    private static readonly HashSet<string> DefaultTransientDownloadDirMarkers = new(StringComparer.Ordinal)
    {
        ".sabnzbd", "__admin__", "_failed_", "_unpack_", "_repair_", "incomplete",
    };

    /// <summary>Whether the path is an existing file with a media extension (the allowlist itself is
    /// <see cref="RemuxRules.MediaExtensions"/>).</summary>
    public static bool IsProcessingMediaCandidate(string path)
    {
        try
        {
            return File.Exists(path) && RemuxRules.MediaExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether the path looks like a download client's in-progress artifact: a hash-named file, or a
    /// file under a folder named by one of the exclude markers.</summary>
    public static bool IsTransientDownloadArtifactMediaPath(string path, IReadOnlyCollection<string>? excludeMarkers)
    {
        var stem = Path.GetFileNameWithoutExtension(path).Trim();
        if (stem.Length is >= 32 and <= 64 && stem.All(Uri.IsHexDigit))
        {
            return true;
        }

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim().ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var markers = excludeMarkers is { Count: > 0 }
            ? excludeMarkers.Select(m => m.Trim().ToLowerInvariant()).Where(m => m.Length > 0).ToHashSet(StringComparer.Ordinal)
            : DefaultTransientDownloadDirMarkers;
        return parts.Overlaps(markers);
    }

    /// <summary>Candidate files under <paramref name="watchedRoot"/>,
    /// plus a count of what the allowlist rejected. Files only; directories are never returned.</summary>
    public static WatchedFolderScanCandidates IterWatchedFolderMediaCandidates(
        string watchedRoot,
        IReadOnlyCollection<string>? mediaExtensions,
        IReadOnlyCollection<string>? excludeMarkers,
        bool excludeHidden,
        bool topLevelOnly)
    {
        var root = Path.GetFullPath(watchedRoot);
        var found = new List<string>();
        var rejected = 0;
        var rejectedSuffixes = new SortedSet<string>(StringComparer.Ordinal);
        HashSet<string>? configuredExtensions = null;
        if (mediaExtensions is { Count: > 0 })
        {
            configuredExtensions = mediaExtensions
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .Select(v => v.StartsWith('.') ? v.ToLowerInvariant() : "." + v.ToLowerInvariant())
                .ToHashSet(StringComparer.Ordinal);
        }

        IEnumerable<string> Walk()
        {
            var option = topLevelOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;
            try
            {
                return Directory.EnumerateFileSystemEntries(root, "*", option);
            }
            catch (IOException)
            {
                return [];
            }
            catch (UnauthorizedAccessException)
            {
                return [];
            }
        }

        foreach (var p in Walk().OrderBy(p => p, StringComparer.Ordinal))
        {
            if (!File.Exists(p))
            {
                continue;
            }

            string relative;
            try
            {
                relative = Path.GetRelativePath(root, Path.GetFullPath(p));
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                continue;
            }

            var relativeParts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (excludeHidden && relativeParts.Any(part => part.StartsWith('.')))
            {
                continue;
            }

            if (IsTransientDownloadArtifactMediaPath(p, excludeMarkers))
            {
                continue;
            }

            var suffix = Path.GetExtension(p).ToLowerInvariant();
            var accepted = configuredExtensions is not null ? configuredExtensions.Contains(suffix) : IsProcessingMediaCandidate(p);
            if (!accepted)
            {
                if (suffix.Length > 0 && !NonMediaCompanionSuffixes.Contains(suffix))
                {
                    rejected++;
                    rejectedSuffixes.Add(suffix);
                }

                continue;
            }

            found.Add(p);
        }

        return new WatchedFolderScanCandidates(found, rejected, [.. rejectedSuffixes]);
    }

    /// <summary>The file's path relative to the watched folder, with forward slashes.</summary>
    public static string RelativePosixPathUnderWatched(string watchedRoot, string filePath) =>
        Path.GetRelativePath(Path.GetFullPath(watchedRoot), Path.GetFullPath(filePath)).Replace('\\', '/');

    /// <summary>Whether a pending or leased remux pass already names this file.</summary>
    public static async Task<bool> ActiveRemuxPassExistsForRelativePathAsync(
        UnitOfWork uow, string relativePosix, string mediaScope, long? libraryId, long? excludeJobId = null)
    {
        var wantScope = ProcessingMediaScopes.Normalize(mediaScope);
        var rows = await uow.QueryAsync(
            "SELECT id, payload_json FROM jobs WHERE job_kind = @kind AND status IN ('pending', 'leased')",
            reader => (Id: reader.GetInt64(0), PayloadJson: reader.IsDBNull(1) ? null : reader.GetString(1)),
            ("@kind", RequeueStore.RemuxPassJobKind)).ConfigureAwait(false);

        foreach (var (id, payloadJson) in rows)
        {
            if (excludeJobId is { } exclude && id == exclude)
            {
                continue;
            }

            if (PayloadNamesFile(payloadJson, relativePosix, wantScope, libraryId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// When a pending remux pass for this file is held back to a later time, that time; otherwise null. A pass is held back
    /// for another look at a file that would not read to the end (#646) or a hand-off waiting out its minimum age (#632).
    /// </summary>
    public static async Task<DateTimeOffset?> HeldBackRemuxPassStartsAtAsync(
        UnitOfWork uow, string relativePosix, string mediaScope, long? libraryId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var wantScope = ProcessingMediaScopes.Normalize(mediaScope);
        var rows = await uow.QueryAsync(
            "SELECT payload_json, not_before FROM jobs WHERE job_kind = @kind AND status = 'pending' " +
            "AND not_before IS NOT NULL AND julianday(not_before) > julianday(@now)",
            reader => (PayloadJson: reader.IsDBNull(0) ? null : reader.GetString(0), StartsAt: PythonTimestamps.Parse(reader.GetValue(1))),
            ("@kind", RequeueStore.RemuxPassJobKind),
            ("@now", PythonTimestamps.Orm(now))).ConfigureAwait(false);

        DateTimeOffset? earliest = null;
        foreach (var (payloadJson, startsAt) in rows)
        {
            if (startsAt is { } at && (earliest is null || at < earliest) && PayloadNamesFile(payloadJson, relativePosix, wantScope, libraryId))
            {
                earliest = at;
            }
        }

        return earliest;
    }

    /// <summary>
    /// The pending or leased remux pass for this file, if any, read inside the caller's write transaction — the
    /// one identity every automatic enqueue path agrees on (library, relative path, scope), so a check and the
    /// insert that depends on it cannot be split by another writer. Oldest first.
    /// </summary>
    internal static ProcessingJob? ActiveRemuxPassForRelativePath(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string relativePosix,
        string mediaScope,
        long? libraryId)
    {
        var wantScope = ProcessingMediaScopes.Normalize(mediaScope);
        return ProcessingJobStore.ActiveOfKind(connection, transaction, RequeueStore.RemuxPassJobKind)
            .FirstOrDefault(job => PayloadNamesFile(job.PayloadJson, relativePosix, wantScope, libraryId));
    }

    /// <summary>Whether a remux-pass payload names this file: same relative path, same scope, and same library when one is given.</summary>
    private static bool PayloadNamesFile(string? payloadJson, string relativePosix, string wantScope, long? libraryId)
    {
        var raw = (payloadJson ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            return false;
        }

        PyJson data;
        try
        {
            data = PyJsonParser.Parse(raw);
        }
        catch (PyJsonDecodeException)
        {
            return false;
        }

        if (data is not PyDict dict)
        {
            return false;
        }

        var rel = dict.Get("relative_media_path") is PyStr relStr ? relStr.Value : null;
        long? jobLibraryId = dict.Get("library_id") is PyInt libInt ? (long)libInt.Value : null;
        var jobScope = dict.Get("media_scope") is PyStr scopeStr ? ProcessingMediaScopes.Normalize(scopeStr.Value) : ProcessingMediaScopes.Movie;
        var sameLibrary = libraryId is null || jobLibraryId == libraryId;
        return rel is not null && rel.Trim() == relativePosix && jobScope == wantScope && sameLibrary;
    }

    private static bool ExistingCompletedOutputPathIsSafe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ExpectedOutputFileForRelativePath(string outputRoot, string relativePosix)
    {
        var root = Path.GetFullPath(outputRoot);
        var parts = relativePosix.Split('/').Where(p => p.Length > 0 && p is not ("." or "..")).ToArray();
        if (parts.Length == 0)
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine([root, .. parts]));
        var relative = Path.GetRelativePath(root, candidate);
        return relative.StartsWith("..", StringComparison.Ordinal) ? null : candidate;
    }

    /// <summary>Whether a completed remux pass for this file left an output that is still on disk. When
    /// <paramref name="sourcePath"/> is given, the completion record must also match the current source.</summary>
    public static async Task<bool> CompletedRemuxOutputExistsForRelativePathAsync(
        UnitOfWork uow,
        string relativePosix,
        string mediaScope,
        long? libraryId,
        string? outputRoot,
        string? sourcePath)
    {
        var wantScope = ProcessingMediaScopes.Normalize(mediaScope);
        var rows = await uow.QueryAsync(
            "SELECT detail FROM activity_events WHERE module = @module AND event_type = @type AND instr(detail, @needle) > 0 " +
            "ORDER BY id DESC LIMIT 50",
            reader => reader.IsDBNull(0) ? null : reader.GetString(0),
            ("@module", "processing"),
            ("@type", ActivityEventTypes.ProcessingFileRemuxPassCompleted),
            ("@needle", relativePosix)).ConfigureAwait(false);

        foreach (var raw in rows)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            PyJson data;
            try
            {
                data = PyJsonParser.Parse(trimmed);
            }
            catch (PyJsonDecodeException)
            {
                continue;
            }

            if (data is not PyDict dict)
            {
                continue;
            }

            if (dict.Get("ok") is not PyBool { Value: true } || dict.Get("source_deleted_after_success") is not PyBool { Value: false })
            {
                continue;
            }

            if (dict.Get("relative_media_path") is not PyStr relStr || relStr.Value != relativePosix)
            {
                continue;
            }

            if (libraryId is { } wantLib && dict.Get("library_id") is PyInt eventLib && (long)eventLib.Value != wantLib)
            {
                continue;
            }

            var jobScope = dict.Get("media_scope") is PyStr scopeStr ? ProcessingMediaScopes.Normalize(scopeStr.Value) : ProcessingMediaScopes.Movie;
            if (jobScope != wantScope)
            {
                continue;
            }

            if (sourcePath is not null && !CompletedEventMatchesCurrentSource(dict, sourcePath))
            {
                continue;
            }

            if (dict.Get("output_file") is not PyStr outputStr || outputStr.Value.Trim().Length == 0)
            {
                continue;
            }

            if (ExistingCompletedOutputPathIsSafe(outputStr.Value))
            {
                return true;
            }
        }

        // An output file by itself is enough to prevent an accidental duplicate remux, but not enough
        // evidence to delete a source that may have been replaced since the history row expired.
        // Cleanup callers provide sourcePath and therefore require a matching completion record above.
        if (outputRoot is not null && sourcePath is null)
        {
            var expected = ExpectedOutputFileForRelativePath(outputRoot, relativePosix);
            if (expected is not null && ExistingCompletedOutputPathIsSafe(expected))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CompletedEventMatchesCurrentSource(PyDict data, string sourcePath)
    {
        FileInfo stat;
        try
        {
            stat = new FileInfo(sourcePath);
            if (!stat.Exists)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        // There is no inode/device fingerprint on Windows, so match on the path and size every
        // completion record carries.
        if (data.Get("inspected_source_path") is PyStr inspected && data.Get("source_size_bytes") is PyInt recordedSize)
        {
            try
            {
                var samePath = string.Equals(Path.GetFullPath(inspected.Value), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase);
                return samePath && recordedSize.Value == stat.Length;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Retries removing a completed movie's source folder. Only the immediate parent folder of a
    /// file under the watched root is removed, never the watched root itself.</summary>
    public static (bool Ok, string? Reason) RetryCompletedMovieSourceCleanup(string watchedRoot, string filePath)
    {
        string root, src;
        try
        {
            root = Path.GetFullPath(watchedRoot);
            src = Path.GetFullPath(filePath);
        }
        catch (ArgumentException exception)
        {
            return (false, $"Source cleanup retry skipped because the path was not safely under the watched folder ({exception.Message}).");
        }

        if (!PathContainment.IsUnder(root, src))
        {
            return (false, "Source cleanup retry skipped because the path was not safely under the watched folder.");
        }

        // The file is strictly inside the root, so its folder is either the root itself or a release folder inside it.
        var movieFolder = Path.GetDirectoryName(src);
        if (movieFolder is null || !PathContainment.IsUnder(root, movieFolder))
        {
            return (false, "Source cleanup retry skipped because the file sits directly in the watched folder root.");
        }

        try
        {
            if (Directory.Exists(movieFolder))
            {
                Directory.Delete(movieFolder, recursive: true);
            }

            return (true, null);
        }
        catch (DirectoryNotFoundException)
        {
            return (true, null);
        }
        catch (IOException exception)
        {
            return (false, $"Source cleanup retry could not remove the release folder because this path is still locked or blocked ({exception.Message}).");
        }
        catch (UnauthorizedAccessException exception)
        {
            return (false, $"Source cleanup retry could not remove the release folder because this path is still locked or blocked ({exception.Message}).");
        }
    }

    /// <summary>
    /// Confirms the source opens for reading and the output folder accepts a write. The read uses .NET's
    /// file-share enforcement (the Win32 <c>CreateFileW(FILE_SHARE_READ|FILE_SHARE_DELETE)</c> probe on Windows).
    /// There is no POSIX <c>flock</c> check: it is advisory and does not catch a writer that never calls
    /// <c>flock</c> itself.
    /// </summary>
    public static (bool Ok, string? Problem) CheckFileAccess(bool skipAccessTests, string filePath, string? outputFolder)
    {
        if (skipAccessTests)
        {
            return (true, null);
        }

        try
        {
            using var handle = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var buffer = new byte[1];
            _ = handle.Read(buffer, 0, 1);
        }
        catch (IOException exception)
        {
            return (false, $"Weir could not open this file for reading — it is usually locked by whatever is still writing it. The system reported: {exception.Message}.");
        }
        catch (UnauthorizedAccessException exception)
        {
            return (false, $"Weir could not open this file for reading — it is usually locked by whatever is still writing it. The system reported: {exception.Message}.");
        }

        if (outputFolder is not null)
        {
            var problem = OutputRootProblem(outputFolder);
            if (problem is not null)
            {
                return (false, problem);
            }
        }

        return (true, null);
    }

    /// <summary>Resolved folders for one scan.</summary>
    public sealed record ProcessingScanPathRuntime(string WatchedFolder, string OutputFolder, string WorkFolderEffective);

    /// <summary>
    /// Resolves one library's folders, or says why they
    /// cannot be used. Reads <c>libraries</c> directly (ADR-0014); no environment path fallback.
    /// </summary>
    public static (ProcessingScanPathRuntime? Runtime, string? Error) ResolvePathRuntimeForLibrary(ProcessingLibraryRecord library, string weirHome)
    {
        ArgumentNullException.ThrowIfNull(library);
        var label = library.Name.Trim().Length > 0 ? library.Name.Trim() : (library.MediaType == ProcessingMediaScopes.Tv ? "TV" : "Movies");

        var watchedRaw = (library.WatchedFolder ?? string.Empty).Trim();
        if (watchedRaw.Length == 0)
        {
            return (null, $"The {label} library has no watched folder set. Manual remux and folder-scan jobs need a watched folder to resolve relative paths. Set it on Processing → Libraries before enqueueing or running those jobs.");
        }

        var watchedPath = ProcessingLibraryFolders.ExpandForFilesystem(watchedRaw);
        if (!Directory.Exists(watchedPath))
        {
            return (null, $"The {label} library's watched folder must be an existing directory.");
        }

        var folderRow = new ProcessingLibraryFolderRow(library.Id, library.MediaType, (int)library.DisplayOrder, library.WorkFolder, library.OutputFolder);
        var workRaw = (library.WorkFolder ?? string.Empty).Trim();
        var workEffectiveRaw = ProcessingLibraryFolders.EffectiveWorkFolder(folderRow, weirHome);
        var workIsDefault = !string.Equals(workEffectiveRaw, workRaw, StringComparison.Ordinal);
        var workPath = ProcessingLibraryFolders.ExpandForFilesystem(workEffectiveRaw);

        var outRaw = (library.OutputFolder ?? string.Empty).Trim();
        if (outRaw.Length == 0)
        {
            return (null, $"The {label} library has no output folder set. Set it on Processing → Libraries before running a live remux pass.");
        }

        var outputPath = ProcessingLibraryFolders.ExpandForFilesystem(outRaw);
        if (!Directory.Exists(outputPath))
        {
            return (null, $"The {label} library's output folder must be an existing directory.");
        }

        var separationError = ValidatePathSeparation(watchedPath, workPath, outputPath);
        if (separationError is not null)
        {
            return (null, separationError);
        }

        if (!workIsDefault && !Directory.Exists(workPath))
        {
            return (null, $"The {label} library's work/temp folder must be an existing directory when set to a custom path.");
        }

        return (new ProcessingScanPathRuntime(watchedPath, outputPath, workPath), null);
    }

    private static bool IsSameOrNested(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var aWithSep = a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var bWithSep = b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return b.StartsWith(aWithSep, StringComparison.OrdinalIgnoreCase) || a.StartsWith(bWithSep, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Why the watched, work and output folders cannot be used together, or null when none overlaps another.</summary>
    private static string? ValidatePathSeparation(string? watched, string work, string? output)
    {
        if (output is not null)
        {
            if (IsSameOrNested(work, output))
            {
                return "The work/temp folder and output folder must be separate (no overlap or containment).";
            }

            if (watched is not null && IsSameOrNested(watched, output))
            {
                return "The watched folder and output folder must be separate (no overlap or containment).";
            }
        }

        if (watched is not null && IsSameOrNested(watched, work))
        {
            return "The watched folder and work/temp folder must be separate (no overlap or containment).";
        }

        return null;
    }

    private static string? OutputRootProblem(string outputFolder)
    {
        var probe = Path.Combine(outputFolder, $".weir-write-test-{Environment.ProcessId}");
        try
        {
            Directory.CreateDirectory(outputFolder);
            File.WriteAllBytes(probe, [0]);
        }
        catch (IOException exception)
        {
            return $"Weir cannot write to the output folder ({outputFolder}), so it did not start work that would have nowhere to go. The system reported: {exception.Message}.";
        }
        catch (UnauthorizedAccessException exception)
        {
            return $"Weir cannot write to the output folder ({outputFolder}), so it did not start work that would have nowhere to go. The system reported: {exception.Message}.";
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
