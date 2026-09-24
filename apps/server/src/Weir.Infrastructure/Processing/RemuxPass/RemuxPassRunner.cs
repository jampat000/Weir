using System.Text.Json;
using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.Metrics;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Rules;
using Weir.Infrastructure.Media;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>
/// Per-file ffprobe, plan, optional ffmpeg remux, publish and post-success cleanup.
/// Returns the result dictionary the handler records; its keys keep a fixed order because it is stored as JSON.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The full-read integrity check runs on every platform, with the probed duration so a truncated Matroska file is
/// caught (#539).</item>
/// <item>The acceleration flags decided here reach the executed ffmpeg command, not only the recorded argv (#539).</item>
/// <item>When the rule set keeps the original language, the metadata lookup decides the preferred audio (#537).</item>
/// </list>
/// </remarks>
public sealed partial class RemuxPassRunner
{
    private const string PlaceholderName = "planned-ffmpeg-destination-placeholder.mkv";

    private readonly MediaTools _tools;
    private readonly IMediaToolResolver _resolver;
    private readonly IRemuxPassFileFacts _facts;
    private readonly OutputFolderCleanup _outputCleanup;
    private readonly ITvSeasonFolderCleanup _tvSeasonCleanup;
    private readonly IOriginalLanguageLookup _originalLanguage;
    private readonly RuntimeMetricsStore? _metrics;
    private readonly RemuxPassSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly IOutputOwnership? _ownership;

    public RemuxPassRunner(
        MediaTools tools,
        IMediaToolResolver resolver,
        IRemuxPassFileFacts facts,
        IPostSuccessCleanupData cleanupData,
        ITvSeasonFolderCleanup tvSeasonCleanup,
        IOriginalLanguageLookup originalLanguage,
        RemuxPassSettings settings,
        TimeProvider time,
        ILogger<RemuxPassRunner> logger,
        RuntimeMetricsStore? metrics = null,
        IOutputOwnership? ownership = null)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _facts = facts ?? throw new ArgumentNullException(nameof(facts));
        _tvSeasonCleanup = tvSeasonCleanup ?? throw new ArgumentNullException(nameof(tvSeasonCleanup));
        _originalLanguage = originalLanguage ?? throw new ArgumentNullException(nameof(originalLanguage));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _ownership = ownership;
        _outputCleanup = new OutputFolderCleanup(cleanupData, time, logger, settings.MovieOutputCleanupMinAgeSeconds, settings.TvOutputCleanupMinAgeSeconds);
    }

    /// <summary>Test seam: whether the Windows hard-link fast path is used.</summary>
    internal bool HardlinkFastPathSupported { get; init; } = OperatingSystem.IsWindows();

    /// <summary>Test seam: free bytes on the volume holding a path.</summary>
    internal Func<string, long>? FreeBytes { get; init; }

    /// <summary>Reserves the source against writers for the complete pass, then runs it.</summary>
    public async Task<PyDict> RunAsync(RemuxPassRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? source;
        try
        {
            source = RemuxPassPaths.ResolveMediaFileUnderRoot(request.Runtime.WatchedFolder, request.RelativeMediaPath);
        }
        catch (ArgumentException)
        {
            source = null;
        }

        if (source is null || !File.Exists(source))
        {
            return await RunInnerAsync(request, new SourceFingerprint(0, 0, 0, 0), cancellationToken).ConfigureAwait(false);
        }

        var (guard, problem) = SourceFiles.AcquireReadGuard(source);
        if (guard is null)
        {
            return SourceNotReady(request.RelativeMediaPath, problem ?? "Weir is waiting until no other program is writing this file.", source);
        }

        using (guard)
        {
            SourceFingerprint fingerprint;
            try
            {
                fingerprint = SourceFiles.Fingerprint(source);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return SourceNotReady(request.RelativeMediaPath, $"Weir could not read this file safely ({exception.Message}), so it will wait.", source);
            }

            return await RunInnerAsync(request, fingerprint, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<PyDict> RunInnerAsync(RemuxPassRequest request, SourceFingerprint expected, CancellationToken cancellationToken)
    {
        var relativeMediaPath = request.RelativeMediaPath;
        var runtime = request.Runtime;
        var scope = ProcessingMediaScopes.Normalize(request.MediaScope);
        var passThrough = request.PassThroughUnchanged;
        var report = request.ProgressReporter;

        string src;
        try
        {
            src = RemuxPassPaths.ResolveMediaFileUnderRoot(runtime.WatchedFolder, relativeMediaPath);
        }
        catch (ArgumentException exception)
        {
            return FailBefore(relativeMediaPath, exception.Message);
        }

        var inspected = src;
        var exists = File.Exists(src) || Directory.Exists(src);
        var isFile = File.Exists(src);
        var suffix = Path.GetExtension(src).ToLowerInvariant();
        if (!exists)
        {
            return FailBefore(
                relativeMediaPath,
                "Weir could not find this file under the saved watched folder. Check the library path or restore the file, then try again.",
                inspected);
        }

        if (!isFile)
        {
            return FailBefore(
                relativeMediaPath,
                "This path is not a regular media file under the saved watched folder. Choose a file, then try again.",
                inspected);
        }

        if (!RemuxRules.MediaExtensions.Contains(suffix))
        {
            return FailBefore(
                relativeMediaPath,
                $"Weir does not process {(suffix.Length > 0 ? suffix : "this")} files in this pass. " +
                "Use a supported media file or update the library's media types, then try again.",
                inspected);
        }

        var (guardrailResult, minAge) = EvaluateSizeAndAgeGuardrails(request, passThrough, src, relativeMediaPath, inspected, scope);
        if (guardrailResult is not null)
        {
            return guardrailResult;
        }

        JsonElement probeJson;
        IReadOnlyList<string> sourceWarnings;
        try
        {
            (probeJson, sourceWarnings) = await _tools.ProbeWithWarningsAsync(src, _settings.ProbeSizeMb, _settings.AnalyzeDurationSeconds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MediaUnreadableException exception)
        {
            return FailBefore(
                relativeMediaPath,
                $"Weir could not read this file's contents, so the file itself looks damaged: {exception.Message}",
                inspected,
                // Evidence the release is bad, so a library set to reject can act on it (#471).
                new PyDict().Set("content_unusable", true));
        }
#pragma warning disable CA1031 // Any ffprobe failure fails this file before anything is written.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return FailBefore(relativeMediaPath, $"ffprobe failed: {exception.Message}", inspected);
        }

        var probe = new ProbeResult(probeJson);
        var (video, audio, subtitles) = RemuxRules.SplitStreams(probe);
        var watchedRoot = RemuxPassPaths.Resolve(runtime.WatchedFolder);
        if (video.Count == 0)
        {
            return FailBefore(
                relativeMediaPath,
                "Weir only processes movie and TV files that contain a video stream. " +
                "This file contains no video, so it was rejected before any output was written.",
                inspected,
                new PyDict()
                    .Set("rejection_kind", "no_video_stream")
                    .Set("media_scope", scope)
                    .Set("processing_watched_folder_resolved", watchedRoot));
        }

        // Measured once and recorded, so the next enqueue can weight this file (#338).
        var (width, height) = RemuxPassMedia.VideoDimensions(video);
        var duration = RemuxPassMedia.ProbeDurationSeconds(probe);
        var codec = PyStrings.Strip(video[0].CodecName);
        await _facts.RecordMeasuredMediaFactsAsync(
            new MeasuredMediaFacts(
                relativeMediaPath,
                width,
                height,
                codec.Length > 0 ? codec : null,
                audio.Count,
                subtitles.Count,
                duration,
                [.. audio.Select(stream => RemuxPassMedia.TruthyText(stream.Get("codec_name")))],
                RemuxPassMedia.VideoBitDepth(video[0]),
                request.LibraryId),
            cancellationToken).ConfigureAwait(false);

        try
        {
            // A full read of its own, before the write reads it again, so a truncated source is caught before anything is written (#539).
            await _tools.ValidateMediaIntegrityAsync(src, duration, cancellationToken).ConfigureAwait(false);
            AssertSourceUnchanged(src, expected);
        }
        catch (MediaCompletenessException exception)
        {
            // #646: named, so the handler can tell this wait (a file that will not read to the end) from the others and
            // stop waiting once the file has sat unchanged through several looks.
            var waiting = SourceNotReady(relativeMediaPath, exception.Message, inspected);
            waiting.Set("not_ready_kind", UnreadableWait);
            return waiting;
        }

        var config = request.RulesConfig ?? RemuxRules.DefaultConfig();
        PyDict? originalLanguage = null;
        if (!passThrough && request.ManualPlan is null && config.OriginalLanguage is { Enabled: true } originalRules)
        {
            (config, originalLanguage) = await ApplyOriginalLanguageAsync(config, originalRules, scope, relativeMediaPath, request.Origin, audio, cancellationToken)
                .ConfigureAwait(false);
        }

        RemuxPlan? plan;
        if (request.ManualPlan is { } manualChoice)
        {
            // Issue #501: the operator's choice is authoritative. A stale fingerprint or an index that is gone
            // (or changed type) both mean the same thing to the operator, so both fail with the same sentence.
            var splitForManual = new SplitProbeStreams(video, audio, subtitles);
            var kinds = ManualTrackPlan.ClassifyIndices(splitForManual);
            var stillValid = request.ManualPlanFingerprint is { } expectedManualFingerprint
                && expectedManualFingerprint == expected
                && ManualTrackPlan.TryValidate(manualChoice, kinds, out _);
            if (!stillValid)
            {
                return FailBefore(relativeMediaPath, ManualTrackPlan.ChangedMessage, inspected);
            }

            plan = ManualTrackPlan.BuildPlan(splitForManual, manualChoice);
        }
        else
        {
            plan = passThrough
                ? RemuxPassMedia.PassThroughPlan(video, audio, subtitles)
                : RemuxRules.PlanRemux(video, audio, subtitles, config, RemuxRules.AttachmentStreams(probe));
        }

        if (plan is null)
        {
            return FailBefore(
                relativeMediaPath,
                "remux plan could not be built (no retainable audio)",
                inspected,
                new PyDict()
                    .Set("rejection_kind", "no_retainable_audio")
                    .Set("media_scope", scope)
                    .Set("processing_watched_folder_resolved", watchedRoot));
        }

        var remuxNeeded = !passThrough && RemuxRules.IsRemuxRequired(plan, audio, subtitles);
        var audioBefore = RemuxDisplay.AudioBeforeLineFromProbe(audio);
        var audioAfter = RemuxDisplay.AudioAfterLineFromPlan(plan);
        var metadataRemoved = RemuxDisplay.MetadataRemovedLineFromPlan(plan);
        var subsBefore = RemuxDisplay.SubtitleBeforeLineFromProbe(subtitles);
        var subsAfter = RemuxDisplay.SubtitleAfterLineFromPlan(plan, !passThrough && config.SubtitleMode == RemuxRuleValues.SubtitleModeRemoveAll);

        var workDir = runtime.WorkFolderEffective;
        AccelerationDecision? hardware = null;
        IReadOnlyList<string> argv = [];
        if (!passThrough)
        {
            var (_, ffmpeg) = _resolver.Resolve();
            var hardwareSettings = new HardwareSettings
            {
                Mode = HardwareAcceleration.NormalizeDecodeMode(runtime.HardwareDecodeMode),
                Device = runtime.HardwareDevice ?? string.Empty,
                DisabledVendors = HardwareAcceleration.ParseDisabledVendors(runtime.HardwareDisabledVendorsCsv),
                Strictness = HardwareAcceleration.NormalizeStrictness(runtime.FfmpegStrictness),
            };
            // Decided before the argv is built, because the flags go into it; a device that is busy or absent falls back (#345).
            hardware = HardwareAcceleration.Decide(
                hardwareSettings,
                hardwareSettings.WantsHardware
                    ? await _tools.KnownAccelerationAsync(ffmpeg, cancellationToken).ConfigureAwait(false)
                    : HardwareAcceleration.NotAsked);
            argv = FfmpegCommands.BuildRemuxArgv(ffmpeg, src, Path.Join(workDir, PlaceholderName), plan, hardware.ArgvFlags);
        }

        var output = new PyDict()
            .Set("ok", true)
            .Set("outcome", RemuxPassOutcomes.LiveOutputWritten)
            .Set("relative_media_path", relativeMediaPath)
            .Set("inspected_source_path", inspected)
            .Set("processing_watched_folder_resolved", watchedRoot)
            .Set("stream_counts", new PyDict().Set("video", video.Count).Set("audio", audio.Count).Set("subtitle", subtitles.Count))
            .Set("preflight_status", "ok")
            .Set("preflight_reason", passThrough
                ? "ffprobe completed and the operator's pass-through request was validated"
                : "ffprobe completed and remux plan was evaluated")
            .Set("preflight_probe_settings", new PyDict().Set("probe_size_mb", _settings.ProbeSizeMb).Set("analyze_duration_seconds", _settings.AnalyzeDurationSeconds))
            .Set("plan_summary", RemuxPassVisibility.SummarizeRemuxPlan(plan))
            .Set("audio_before", audioBefore)
            .Set("audio_after", audioAfter)
            .Set("subs_before", subsBefore)
            .Set("subs_after", subsAfter)
            .Set("removed_audio", StringList(plan.RemovedAudio))
            .Set("removed_subtitles", StringList(plan.RemovedSubtitles))
            .Set("removed_images", StringList(plan.RemovedImages))
            .Set("removed_attachments", StringList(plan.RemovedAttachments))
            .Set("metadata_removed", metadataRemoved)
            .Set("remux_required", remuxNeeded)
            .Set("pass_through_unchanged", passThrough)
            .Set("ffmpeg_argv", StringList(argv))
            .Set("audio_selection_notes", StringList(plan.AudioSelectionNotes))
            .Set("media_scope", scope)
            .Set("source_fingerprint", new PyDict()
                .Set("device", new PyInt(expected.Device))
                .Set("inode", new PyInt(expected.Inode))
                .Set("size_bytes", expected.SizeBytes)
                .Set("modified_time_ns", expected.ModifiedTimeNs));
        if (originalLanguage is not null)
        {
            output.Set("original_language", originalLanguage);
        }

        var collisionPolicy = OutputCollision.NormalizePolicy(runtime.OutputCollisionPolicy);
        var sidecarPatterns = SidecarMigration.ParsePatterns(runtime.SidecarPatternsCsv);
        var outDir = RemuxPassPaths.Resolve(runtime.OutputFolder);

        if (runtime.WorkFolderIsDefault)
        {
            Directory.CreateDirectory(workDir);
        }
        else if (!Directory.Exists(workDir))
        {
            return FailBefore(relativeMediaPath, "The work/temp folder is missing on disk (custom path must exist before a live pass).", inspected);
        }

        var relative = RemuxPassPaths.RelativeTo(src, watchedRoot)!;

        // What this pass cleaned, as measured before any work started (the pass refuses to publish if it changed since).
        // A successful pass records it on the file, so a library that keeps originals recognises the same file next scan.
        output.Set("source_fingerprint_size", expected.SizeBytes);
        output.Set("source_fingerprint_mtime_ns", expected.ModifiedTimeNs);
        var context = new PassContext(request, src, inspected, scope, watchedRoot, outDir, expected, audio.Count, duration, minAge, plan, probeJson, sourceWarnings);
        if (!remuxNeeded)
        {
            return await PlaceUnchangedAsync(context, output, plan, argv, audioBefore, audioAfter, subsBefore, subsAfter, relative, collisionPolicy, sidecarPatterns, cancellationToken)
                .ConfigureAwait(false);
        }

        return await RemuxAndPublishAsync(context, output, plan, argv, hardware!, audioBefore, audioAfter, subsBefore, subsAfter, relative, collisionPolicy, sidecarPatterns, cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed record PassContext(
        RemuxPassRequest Request,
        string Source,
        string Inspected,
        string Scope,
        string WatchedRoot,
        string OutputDirectory,
        SourceFingerprint Expected,
        int ExpectedAudio,
        double? Duration,
        long MinAge,
        RemuxPlan Plan,
        JsonElement SourceProbe,
        IReadOnlyList<string> SourceWarnings)
    {
        public string RelativeMediaPath => Request.RelativeMediaPath;
    }

}
