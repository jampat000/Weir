namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>
/// The real ffprobe and ffmpeg, for the few scenarios that need genuine media tools on tiny generated files. They are found on
/// PATH, or in <c>WEIR_CONTRACT_REAL_FFMPEG_DIR</c> when that names a folder; a scenario marked <see cref="RealFfmpegFactAttribute"/>
/// or <see cref="RealFfmpegTheoryAttribute"/> is skipped when they are missing.
/// </summary>
public static class RealFfmpeg
{
    public const string DirectoryVariable = "WEIR_CONTRACT_REAL_FFMPEG_DIR";

    public const string MissingReason = "ffmpeg and ffprobe are not on PATH (or WEIR_CONTRACT_REAL_FFMPEG_DIR)";

    private static readonly Lazy<string?> Located = new(Locate);

    /// <summary>A folder holding both tools, or null.</summary>
    public static string? Folder => Located.Value;

    public static bool IsAvailable => Folder is not null;

    /// <summary>What the server needs to find the tools: pass it to <see cref="WeirServer.StartNewAsync"/>.</summary>
    public static IReadOnlyDictionary<string, string> Env =>
        new Dictionary<string, string> { ["WEIR_FFMPEG_DIR"] = Folder ?? throw new InvalidOperationException(MissingReason) };

    private static string? Locate()
    {
        var explicitFolder = Environment.GetEnvironmentVariable(DirectoryVariable)?.Trim();
        if (!string.IsNullOrEmpty(explicitFolder))
        {
            return Directory.Exists(explicitFolder) ? explicitFolder : null;
        }

        var ffmpeg = OnPath("ffmpeg");
        var ffprobe = OnPath("ffprobe");
        if (ffmpeg is null || ffprobe is null)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(Resolved(ffmpeg));
        return folder == Path.GetDirectoryName(Resolved(ffprobe)) ? folder : null;
    }

    private static string Resolved(string path) =>
        File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);

    private static string? OnPath(string program)
    {
        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return folders
            .SelectMany(folder => extensions.Select(extension => Path.Combine(folder, program + extension)))
            .FirstOrDefault(File.Exists);
    }
}

/// <summary>A test that uses the real ffmpeg and ffprobe; it is skipped when they are not available.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RealFfmpegFactAttribute : FactAttribute
{
    public RealFfmpegFactAttribute()
    {
        if (!RealFfmpeg.IsAvailable)
        {
            Skip = RealFfmpeg.MissingReason;
        }
    }
}

/// <summary>A parametrised test that uses the real ffmpeg and ffprobe; it is skipped when they are not available.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RealFfmpegTheoryAttribute : TheoryAttribute
{
    public RealFfmpegTheoryAttribute()
    {
        if (!RealFfmpeg.IsAvailable)
        {
            Skip = RealFfmpeg.MissingReason;
        }
    }
}
