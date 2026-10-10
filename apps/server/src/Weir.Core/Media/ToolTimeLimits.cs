namespace Weir.Core.Media;

/// <summary>
/// How long a media tool may take on a file. A run is stopped for going silent, which no healthy run does, and for
/// outlasting a bound that grows with the size of the file, so a big file on a slow drive or a busy machine gets the time
/// it needs and only a run that has stopped is cut off.
/// </summary>
public static class ToolTimeLimits
{
    /// <summary>
    /// How long a tool that reports progress may say nothing before it is taken to have stopped. ffmpeg reports twice a
    /// second and mkvmerge at every percent, so ten minutes of silence is a read or a write that is not moving: a share that
    /// has gone away, a drive that has hung.
    /// </summary>
    public const int SilenceSeconds = 600;

    /// <summary>
    /// How long ffmpeg is given to exit once it has printed <c>progress=end</c>, which it does after the output is complete. A
    /// process that is still there then has nothing left to do, and is stopped.
    /// </summary>
    public const int FinishedExitSeconds = 60;

    /// <summary>The slowest sustained rate a read or a write of a whole file is allowed to run at: 1 MiB a second.</summary>
    public const long SlowestBytesPerSecond = 1024 * 1024;

    /// <summary>
    /// The longest a tool may spend reading or writing <paramref name="bytes"/>: <see cref="FfmpegCommands.FfmpegTimeoutSeconds"/>
    /// plus the time the file takes at <see cref="SlowestBytesPerSecond"/>.
    /// </summary>
    public static int OverallSeconds(long bytes) =>
        (int)Math.Min(int.MaxValue, FfmpegCommands.FfmpegTimeoutSeconds + Math.Max(0, bytes) / SlowestBytesPerSecond);

    /// <summary>
    /// How long a tool that reports only whole percents may need to get from one to the next: twice the time one percent of
    /// <paramref name="bytes"/> takes at <see cref="SlowestBytesPerSecond"/>, so a source of 60 GB or more, going as slowly as a run is
    /// allowed to, is not taken for a stalled one. The silence limit for such a tool is this or <see cref="SilenceSeconds"/>, whichever is longer.
    /// </summary>
    public static int PercentStepSeconds(long bytes) =>
        (int)Math.Min(int.MaxValue, 2 * (Math.Max(0, bytes) / 100) / SlowestBytesPerSecond);

    /// <summary>
    /// The longest ffprobe may spend: its usual limit, which covers the default probe size, plus the time it takes to read
    /// the megabytes of <paramref name="probeSizeMb"/> beyond that at <see cref="SlowestBytesPerSecond"/>, since a probe size
    /// the operator raised is read in full.
    /// </summary>
    public static int ProbeSeconds(int probeSizeMb) =>
        FfmpegCommands.FfprobeTimeoutSeconds + Math.Max(0, probeSizeMb - FfmpegCommands.DefaultProbeSizeMb);
}
