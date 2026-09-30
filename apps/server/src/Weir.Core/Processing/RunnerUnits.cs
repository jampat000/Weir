using System.Text.Json;
using Weir.Core.Processing.RemuxPass;
using Weir.Core.Rules;

namespace Weir.Core.Processing;

/// <summary>Classify a video's resolution (sd/720p/1080p/4k) from its width, falling back to its height.</summary>
public static class RunnerUnits
{
    /// <summary>The class of a file whose resolution is not known; <see cref="Jobs.RunnerBudget"/> costs it like 1080p.</summary>
    public const string UndeterminedClass = "undetermined";

    private static readonly (int Ceiling, string Name)[] ClassByWidth = [(1200, "sd"), (1900, "720p"), (2600, "1080p")];
    private static readonly (int Ceiling, string Name)[] ClassByHeight = [(700, "sd"), (1000, "720p"), (1500, "1080p")];

    public static string ResolutionClassForDimensions(long? width, long? height)
    {
        if (width is { } w && w > 0)
        {
            foreach (var (ceiling, name) in ClassByWidth)
            {
                if (w < ceiling)
                {
                    return name;
                }
            }

            return "4k";
        }

        if (height is not { } h || h <= 0)
        {
            return UndeterminedClass;
        }

        foreach (var (ceiling, name) in ClassByHeight)
        {
            if (h < ceiling)
            {
                return name;
            }
        }

        return "4k";
    }

    /// <summary>The class of the largest video stream in an ffprobe document, or <see cref="UndeterminedClass"/> when there is none or it cannot be read.</summary>
    public static string ResolutionClassForProbeJson(string? probeJson)
    {
        if (string.IsNullOrWhiteSpace(probeJson))
        {
            return UndeterminedClass;
        }

        try
        {
            return ResolutionClassForProbe(ProbeResult.Parse(probeJson));
        }
        catch (JsonException)
        {
            return UndeterminedClass;
        }
    }

    /// <summary>The class of the largest video stream in a probe.</summary>
    public static string ResolutionClassForProbe(ProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var (width, height) = RemuxPassMedia.VideoDimensions(RemuxRules.SplitStreams(probe).Video);
        return ResolutionClassForDimensions(width, height);
    }
}
