using Microsoft.Extensions.Logging;
using Weir.Core.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Media;

/// <summary>
/// ffprobe and ffmpeg execution: probing, output validation, the full-read integrity check, remuxing with progress, and hardware detection. Decisions live in
/// <see cref="Weir.Core.Media"/>; this class runs the tools and hands their output over.
/// </summary>
/// <remarks>
/// Split by concern across several partial-class files, one type per concern: probing
/// (<c>MediaTools.Probing.cs</c>), file state (<c>MediaTools.FileInspection.cs</c>), the media integrity check
/// (<c>MediaTools.Integrity.cs</c>), running ffmpeg itself (<c>MediaTools.Ffmpeg.cs</c>), remuxing to a temp file
/// and validating the result (<c>MediaTools.Remux.cs</c>), hardware acceleration detection
/// (<c>MediaTools.Acceleration.cs</c>), tool version reporting (<c>MediaTools.Versions.cs</c>) and mkvmerge
/// (<c>MediaTools.Mkvmerge.cs</c>). This file holds only the fields, constructors and the one logger shared by
/// both external tools' debug summaries.
/// </remarks>
public sealed partial class MediaTools
{
    private readonly IProcessRunner _runner;
    private readonly IMediaToolResolver _resolver;
    private readonly ILogger<MediaTools> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly bool _windows = OperatingSystem.IsWindows();
    private readonly Func<string, MediaFileState> _inspectFile;

    public MediaTools(IProcessRunner runner, IMediaToolResolver resolver, ILogger<MediaTools> logger, TimeProvider timeProvider)
        : this(runner, resolver, logger, timeProvider, InspectFile)
    {
    }

    /// <summary>For tests: file state comes from <paramref name="inspectFile"/> instead of the filesystem.</summary>
    internal MediaTools(IProcessRunner runner, IMediaToolResolver resolver, ILogger<MediaTools> logger, TimeProvider timeProvider, Func<string, MediaFileState> inspectFile)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(inspectFile);
        _inspectFile = inspectFile;
        _runner = runner;
        _resolver = resolver;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// <c>logger.debug</c>'s one-line summary of an argv about to run, shared by the ffmpeg and mkvmerge remux
    /// writes (<see cref="FfmpegCommands.DebugSummary"/> and <see cref="MkvmergeCommands.DebugSummary"/>).
    /// </summary>
    [LoggerMessage(Level = LogLevel.Debug, Message = "{Summary}")]
    private partial void LogFfmpegDebug(string summary);
}
