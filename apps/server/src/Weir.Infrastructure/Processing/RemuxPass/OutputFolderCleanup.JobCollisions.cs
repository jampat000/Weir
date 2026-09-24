using Weir.Core.Json;
using Weir.Core.Processing;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class OutputFolderCleanup
{
    private async Task<bool> MovieJobBlocksAsync(string relNorm, long? currentJobId, CancellationToken cancellationToken)
    {
        foreach (var job in await _data.ActiveRemuxJobsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (currentJobId is { } exclude && job.Id == exclude)
            {
                continue;
            }

            var scope = "movie";
            var jobRel = string.Empty;
            if (job.PayloadJson is { } raw && PyStrings.Strip(raw).Length > 0)
            {
                PyJson data;
                try
                {
                    data = PyJsonParser.Parse(raw);
                }
                catch (PyJsonDecodeException)
                {
                    continue;
                }

                if (data is PyDict dict)
                {
                    scope = ProcessingMediaScopes.Normalize((dict.Get("media_scope") as PyStr)?.Value);
                    if (dict.Get("relative_media_path") is PyStr jr && PyStrings.Strip(jr.Value).Length > 0)
                    {
                        jobRel = NormalizeRelativeForMatch(jr.Value);
                    }
                }
            }

            if (scope == "tv")
            {
                continue;
            }

            if (jobRel.Length > 0 && jobRel == relNorm)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TvJobBlocksAsync(string outputRoot, string seasonFolder, long? currentJobId, CancellationToken cancellationToken)
    {
        foreach (var job in await _data.ActiveRemuxJobsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (currentJobId is { } exclude && job.Id == exclude)
            {
                continue;
            }

            var scope = "movie";
            var jobRel = string.Empty;
            if (job.PayloadJson is { } raw && PyStrings.Strip(raw).Length > 0)
            {
                PyJson data;
                try
                {
                    data = PyJsonParser.Parse(raw);
                }
                catch (PyJsonDecodeException)
                {
                    continue;
                }

                if (data is PyDict dict)
                {
                    scope = dict.Get("media_scope") is PyStr js && string.Equals(PyStrings.Strip(js.Value), "tv", StringComparison.OrdinalIgnoreCase) ? "tv" : "movie";
                    if (dict.Get("relative_media_path") is PyStr jr && PyStrings.Strip(jr.Value).Length > 0)
                    {
                        jobRel = PyStrings.Strip(jr.Value);
                    }
                }
            }

            if (scope != "tv")
            {
                continue;
            }

            var relNorm = NormalizeRelativeForMatch(jobRel);
            if (relNorm.Length == 0)
            {
                continue;
            }

            var expected = RemuxPassPaths.Resolve(Path.Join(outputRoot, relNorm));
            if (!RemuxPassPaths.IsUnder(expected, outputRoot))
            {
                continue;
            }

            if (Path.GetDirectoryName(expected) is { } jobSeason && RemuxPassPaths.SamePath(jobSeason, seasonFolder))
            {
                return true;
            }
        }

        return false;
    }
}
