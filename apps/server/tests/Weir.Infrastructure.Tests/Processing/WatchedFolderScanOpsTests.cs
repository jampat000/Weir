using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>Real-temp-dir, real-SQLite tests of the watched-folder scan dispatch operations.</summary>
public sealed class WatchedFolderScanOpsTests
{
    [Fact]
    public void Candidates_are_returned_sorted_and_non_media_is_skipped()
    {
        using var dir = new TempDirectory();
        var w = dir.Join("w");
        Directory.CreateDirectory(w);
        File.WriteAllBytes(Path.Combine(w, "b.mkv"), [1]);
        File.WriteAllBytes(Path.Combine(w, "a.mkv"), [1]);
        File.WriteAllBytes(Path.Combine(w, "skip.txt"), [1]);

        var result = WatchedFolderListing.Candidates(w, null, null, excludeHidden: false, topLevelOnly: false);

        Assert.Equal(["a.mkv", "b.mkv"], result.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void A_downloader_hash_artifact_beside_the_real_file_is_skipped()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        var release = Path.Combine(watched, "Fair.Game.2010.1080p.HMAX.WEB-DL.DDP5.1.x264-PTerWEB");
        Directory.CreateDirectory(release);
        var final = Path.Combine(release, "Fair.Game.2010.1080p.HMAX.WEB-DL.DDP5.1.x264-PTerWEB.mkv");
        var transient = Path.Combine(release, "f25fd97b42a546f08a49e40a39b46e8d.mkv");
        File.WriteAllBytes(final, "final"u8.ToArray());
        File.WriteAllBytes(transient, "hash"u8.ToArray());

        var result = WatchedFolderListing.Candidates(watched, null, null, excludeHidden: false, topLevelOnly: false);

        Assert.Equal([final], result.Files);
    }

    [Fact]
    public void The_walk_reports_each_files_size_and_times_as_the_file_itself_does()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        Directory.CreateDirectory(watched);
        var film = Path.Combine(watched, "Film.mkv");
        File.WriteAllBytes(film, new byte[1234]);
        File.SetLastWriteTimeUtc(film, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));

        var entry = Assert.Single(WatchedFolderListing.Candidates(watched, null, null, excludeHidden: false, topLevelOnly: false).Entries);

        Assert.Equal(WatchedMediaFile.Stat(film), entry);
        Assert.Equal(1234, entry.SizeBytes);
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc), entry.ModifiedUtc);
    }

    [Fact]
    public void Relative_posix_path_uses_forward_slashes_regardless_of_platform()
    {
        using var dir = new TempDirectory();
        var root = dir.Join("root");
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);
        var f = Path.Combine(sub, "f.mkv");
        File.WriteAllBytes(f, [1]);

        Assert.Equal("sub/f.mkv", WatchedFolderScanOps.RelativePosixPathUnderWatched(root, f));
    }

    private static long InsertPendingRemuxPassJob(JobsTestDatabase db, string? payloadJson, string status = "pending")
    {
        db.Execute(
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, max_attempts, attempt_count) " +
            "VALUES (@dedupe, @kind, @payload, @status, 3, @attempts)",
            ("@dedupe", "wf-" + Guid.NewGuid().ToString("N")),
            ("@kind", "processing.file.remux_pass.v1"),
            ("@payload", payloadJson),
            ("@status", status),
            ("@attempts", status == "leased" ? 1L : 0L));
        return Convert.ToInt64(db.Scalar("SELECT last_insert_rowid()"));
    }

    [Fact]
    public async Task Active_remux_pass_is_detected_by_relative_path_and_scoped_by_media_scope()
    {
        using var db = new JobsTestDatabase();
        InsertPendingRemuxPassJob(db, "{\"relative_media_path\":\"movies/a.mkv\",\"media_scope\":\"movie\"}");
        await using var uow = await UnitOfWork.OpenAsync(db.Database);

        Assert.True(await ActiveRemuxPasses.ExistsForRelativePathAsync(uow, "movies/a.mkv", "movie", null));
        Assert.False(await ActiveRemuxPasses.ExistsForRelativePathAsync(uow, "other.mkv", "movie", null));
        Assert.False(await ActiveRemuxPasses.ExistsForRelativePathAsync(uow, "movies/a.mkv", "tv", null));
    }

    [Fact]
    public async Task Completed_remux_output_blocks_repeat_scan_when_source_cleanup_failed()
    {
        using var dir = new TempDirectory();
        using var db = new JobsTestDatabase();
        var output = dir.Join("out", "movies", "a.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, "done"u8.ToArray());
        var source = dir.Join("source", "movies", "a.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, "source"u8.ToArray());

        var detail = $$"""
            {"ok":true,"relative_media_path":"movies/a.mkv","media_scope":"movie","output_file":"{{output.Replace("\\", "\\\\")}}",
            "source_deleted_after_success":false,"inspected_source_path":"{{source.Replace("\\", "\\\\")}}","source_size_bytes":{{new FileInfo(source).Length}}}
            """.Replace("\n", string.Empty);
        db.Execute(
            "INSERT INTO activity_events (module, event_type, title, detail, relative_path) " +
            "VALUES ('processing', 'processing.file_remux_pass_completed', 'x', @detail, json_extract(@detail, '$.relative_media_path'))",
            ("@detail", detail));

        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        Assert.True(await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, "movies/a.mkv", "movie", null, null, source));
        Assert.False(await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, "movies/a.mkv", "tv", null, null, source));
    }

    [Fact]
    public async Task Completed_remux_output_guard_allows_reprocess_when_output_missing()
    {
        using var dir = new TempDirectory();
        using var db = new JobsTestDatabase();
        var missing = dir.Join("missing", "a.mkv");
        var detail = $$"""{"ok":true,"relative_media_path":"movies/a.mkv","media_scope":"movie","output_file":"{{missing.Replace("\\", "\\\\")}}","source_deleted_after_success":false}""";
        db.Execute(
            "INSERT INTO activity_events (module, event_type, title, detail, relative_path) " +
            "VALUES ('processing', 'processing.file_remux_pass_completed', 'x', @detail, json_extract(@detail, '$.relative_media_path'))",
            ("@detail", detail));

        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        Assert.False(await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, "movies/a.mkv", "movie", null, null, null));
    }

    [Fact]
    public async Task Completed_remux_output_never_authorizes_cleanup_for_a_changed_source()
    {
        using var dir = new TempDirectory();
        using var db = new JobsTestDatabase();
        var source = dir.Join("watch", "Movie", "Movie.mkv");
        var output = dir.Join("out", "Movie", "Movie.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(source, "replacement-source"u8.ToArray());
        File.WriteAllBytes(output, "old-output"u8.ToArray());

        // Recorded size (8, "original") does not match the file now on disk.
        var detail = $$"""
            {"ok":true,"relative_media_path":"Movie/Movie.mkv","media_scope":"movie","output_file":"{{output.Replace("\\", "\\\\")}}",
            "source_deleted_after_success":false,"inspected_source_path":"{{source.Replace("\\", "\\\\")}}","source_size_bytes":8}
            """.Replace("\n", string.Empty);
        db.Execute(
            "INSERT INTO activity_events (module, event_type, title, detail, relative_path) " +
            "VALUES ('processing', 'processing.file_remux_pass_completed', 'x', @detail, json_extract(@detail, '$.relative_media_path'))",
            ("@detail", detail));

        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var outputRoot = dir.Join("out");
        Assert.False(await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, "Movie/Movie.mkv", "movie", null, outputRoot, source));
    }

    [Fact]
    public async Task An_output_root_file_alone_blocks_a_repeat_scan_after_history_expired()
    {
        using var dir = new TempDirectory();
        using var db = new JobsTestDatabase();
        var output = dir.Join("out", "movies", "a.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, "done"u8.ToArray());

        await using var uow = await UnitOfWork.OpenAsync(db.Database);
        var outputRoot = dir.Join("out");
        Assert.True(await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, "movies/a.mkv", "movie", null, outputRoot, null));
        Assert.False(await WatchedFolderScanOps.CompletedRemuxOutputExistsForRelativePathAsync(uow, "movies/missing.mkv", "movie", null, outputRoot, null));
    }

    [Fact]
    public void Retry_completed_movie_source_cleanup_removes_the_release_folder()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        var release = Path.Combine(watched, "Movie 2026");
        Directory.CreateDirectory(release);
        var media = Path.Combine(release, "Movie 2026.mkv");
        File.WriteAllBytes(media, "source"u8.ToArray());
        File.WriteAllText(Path.Combine(release, "extra.nfo"), "metadata");

        var (ok, _, reason) = WatchedFolderScanOps.RetryCompletedMovieSourceCleanup(watched, media, null);

        Assert.True(ok);
        Assert.Null(reason);
        Assert.False(Directory.Exists(release));
    }

    [Fact]
    public void Retry_completed_movie_source_cleanup_never_removes_a_watched_root_file()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        Directory.CreateDirectory(watched);
        var media = Path.Combine(watched, "Loose Movie 2026.mkv");
        File.WriteAllBytes(media, "source"u8.ToArray());

        var (ok, _, reason) = WatchedFolderScanOps.RetryCompletedMovieSourceCleanup(watched, media, null);

        Assert.False(ok);
        Assert.Contains("watched folder root", reason ?? string.Empty, StringComparison.Ordinal);
        Assert.True(File.Exists(media));
    }

    [Fact]
    public void Retry_completed_movie_source_cleanup_in_a_pack_removes_only_the_finished_film()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        var pack = Path.Combine(watched, "Collection");
        Directory.CreateDirectory(pack);
        var media = Path.Combine(pack, "Part One.mkv");
        var sibling = Path.Combine(pack, "Part Two.mkv");
        File.WriteAllBytes(media, "source"u8.ToArray());
        File.WriteAllBytes(sibling, "another film"u8.ToArray());

        var (ok, folderRemoved, reason) = WatchedFolderScanOps.RetryCompletedMovieSourceCleanup(watched, media, null);

        Assert.True(ok);
        Assert.False(folderRemoved);
        Assert.Equal(ReleaseFolderRemoval.OtherVideosReason, reason);
        Assert.False(File.Exists(media));
        Assert.True(File.Exists(sibling));
    }

    [Fact]
    public void Retry_completed_movie_source_cleanup_never_removes_a_sibling_folder_that_shares_the_prefix()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        Directory.CreateDirectory(watched);
        var release = Path.Combine(dir.Join("watch-old"), "Movie 2026");
        Directory.CreateDirectory(release);
        var media = Path.Combine(release, "Movie 2026.mkv");
        File.WriteAllBytes(media, "source"u8.ToArray());

        var (ok, _, reason) = WatchedFolderScanOps.RetryCompletedMovieSourceCleanup(watched, media, null);

        Assert.False(ok);
        Assert.Contains("not safely under the watched folder", reason ?? string.Empty, StringComparison.Ordinal);
        Assert.True(File.Exists(media));
    }

    [Fact]
    public void Cleanup_rejected_file_never_deletes_from_a_sibling_folder_that_shares_the_prefix()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        Directory.CreateDirectory(watched);
        var sibling = dir.Join("watch-old");
        Directory.CreateDirectory(sibling);
        var media = Path.Combine(sibling, "bad.mkv");
        File.WriteAllBytes(media, [1]);

        var (deleted, detail) = RemuxPassPaths.CleanupRejectedFile(watched, media, "delete_file");

        Assert.False(deleted);
        Assert.Contains("not safely inside the watched folder", detail, StringComparison.Ordinal);
        Assert.True(File.Exists(media));
    }

    [Fact]
    public void Cleanup_rejected_file_reports_a_missing_file_in_plain_words()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        Directory.CreateDirectory(watched);
        var media = Path.Combine(watched, "gone.mkv");

        var (deleted, detail) = RemuxPassPaths.CleanupRejectedFile(watched, media, "delete_file");

        Assert.False(deleted);
        Assert.Equal($"Weir did not delete the rejected file because {media} could not be found.", detail);
    }

    [Fact]
    public void Cleanup_rejected_file_leaves_the_file_alone_under_the_leave_policy()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        Directory.CreateDirectory(watched);
        var media = Path.Combine(watched, "bad.mkv");
        File.WriteAllBytes(media, [1]);

        var (deleted, detail) = RemuxPassPaths.CleanupRejectedFile(watched, media, "leave");

        Assert.False(deleted);
        Assert.Contains("Leave in place", detail, StringComparison.Ordinal);
        Assert.True(File.Exists(media));
    }

    [Fact]
    public void Cleanup_rejected_file_deletes_under_the_delete_file_policy_and_prunes_the_empty_parent()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watch");
        var release = Path.Combine(watched, "Bad Release");
        Directory.CreateDirectory(release);
        var media = Path.Combine(release, "bad.mkv");
        File.WriteAllBytes(media, [1]);

        var (deleted, detail) = RemuxPassPaths.CleanupRejectedFile(watched, media, "delete_file");

        Assert.True(deleted);
        Assert.Contains("Delete rejected file", detail, StringComparison.Ordinal);
        Assert.False(File.Exists(media));
        Assert.False(Directory.Exists(release));
    }

    [Fact]
    public void Resolve_path_runtime_requires_an_existing_watched_folder()
    {
        var library = new ProcessingLibraryRecord { Name = "Movies", MediaType = ProcessingMediaScopes.Movie, WatchedFolder = string.Empty, OutputFolder = string.Empty };
        var (runtime, error) = WatchedFolderScanOps.ResolvePathRuntimeForLibrary(library, Path.GetTempPath());

        Assert.Null(runtime);
        Assert.Contains("no watched folder set", error ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_path_runtime_succeeds_for_a_library_with_real_watched_and_output_folders()
    {
        using var dir = new TempDirectory();
        var watched = dir.Join("watched");
        var output = dir.Join("output");
        Directory.CreateDirectory(watched);
        Directory.CreateDirectory(output);
        var library = new ProcessingLibraryRecord { Name = "Movies", MediaType = ProcessingMediaScopes.Movie, WatchedFolder = watched, OutputFolder = output, WorkFolder = string.Empty };

        var (runtime, error) = WatchedFolderScanOps.ResolvePathRuntimeForLibrary(library, dir.Path);

        Assert.Null(error);
        Assert.NotNull(runtime);
        Assert.Equal(Path.GetFullPath(watched), runtime!.WatchedFolder);
        Assert.Equal(Path.GetFullPath(output), runtime.OutputFolder);
    }
}
