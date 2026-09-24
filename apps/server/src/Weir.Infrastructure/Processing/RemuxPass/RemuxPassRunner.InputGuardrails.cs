using System.Globalization;
using Weir.Core.Json;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassRunner
{
    /// <summary>
    /// The two preflight checks that run before ffprobe: minimum input size and minimum file age. Returns the pass's
    /// result when a guardrail fires, or <see langword="null"/> alongside the resolved minimum age to continue with.
    /// </summary>
    private (WireObject? Result, long MinAge) EvaluateSizeAndAgeGuardrails(
        RemuxPassRequest request,
        bool passThrough,
        string src,
        string relativeMediaPath,
        string inspected,
        string scope)
    {
        var minSizeMb = Math.Max(0, request.MinInputFileSizeMb);
        if (minSizeMb > 0 && !passThrough)
        {
            long sourceSize;
            try
            {
                sourceSize = new FileInfo(src).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return (FailBefore(relativeMediaPath, $"Weir could not read the source file size: {exception.Message}", inspected), 0);
            }

            var sourceMb = FileLifecycle.BytesToMb(sourceSize);
            if (sourceSize < minSizeMb * 1024 * 1024)
            {
                return (SkipGuardrail(
                    relativeMediaPath,
                    $"Skipped: file below minimum size ({sourceMb.ToString("F1", CultureInfo.InvariantCulture)} MB < {minSizeMb.ToString(CultureInfo.InvariantCulture)} MB).",
                    "minimum_input_file_size",
                    inspected,
                    new WireObject()
                        .Set("source_size_bytes", sourceSize)
                        .Set("source_size_mb", Math.Round(sourceMb, 1, MidpointRounding.ToEven))
                        .Set("minimum_input_file_size_mb", minSizeMb)
                        .Set("media_scope", scope)), 0);
            }
        }

        var minAge = Math.Max(0, request.MinFileAgeSeconds ?? _settings.WatchedFolderMinFileAgeSeconds);
        if (minAge > 0)
        {
            double age;
            try
            {
                age = (_time.GetUtcNow() - new DateTimeOffset(File.GetLastWriteTimeUtc(src))).TotalSeconds;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                age = -1;
            }

            if (age < minAge)
            {
                // Not a failure: the guardrail is a wait, and it ends by itself (#632). A preflight failure is never
                // retried and runs the library's failure policy at once, yet a media manager hands a file over within
                // seconds of the download finishing, so a good release would be passed through unprocessed or, under
                // "reject", reported bad. The handler looks again once the file is old enough
                // (RemuxPassHandler.DeferUntilOldEnoughAsync).
                var known = Math.Max(0, age);
                var remaining = (long)Math.Ceiling(minAge - known);
                var waiting = SourceNotReady(
                    relativeMediaPath,
                    $"This file changed too recently. Weir waits {minAge.ToString(CultureInfo.InvariantCulture)}s after the last change " +
                    $"before processing, so it has about {remaining.ToString(CultureInfo.InvariantCulture)}s to go.",
                    inspected);
                waiting.Set("not_ready_kind", MinimumAgeWait);
                waiting.Set("not_ready_seconds", remaining);
                return (waiting, minAge);
            }
        }

        return (null, minAge);
    }
}
