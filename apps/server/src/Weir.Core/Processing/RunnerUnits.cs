namespace Weir.Core.Processing;

/// <summary>Classify a video's resolution (sd/720p/1080p/4k) from its width, falling back to its height.</summary>
public static class RunnerUnits
{
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
            return "undetermined";
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
}
