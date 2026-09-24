using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Weir.Core.Media;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Media;

/// <summary>A file's ffprobe JSON and the warnings ffprobe printed while reading it (#500's baseline).</summary>
public sealed record ProbeWithWarnings(JsonElement Probe, IReadOnlyList<string> Warnings);

/// <summary>ffprobe: a file's streams and format, and the warnings it prints on the way.</summary>
public sealed partial class MediaTools
{
    /// <summary>
    /// Probes <paramref name="path"/> and returns ffprobe's JSON object. Throws
    /// <see cref="MediaUnreadableException"/> when ffprobe says the contents are unreadable,
    /// <see cref="MediaToolException"/> for any other failure, and <see cref="MediaToolTimeoutException"/> on timeout.
    /// </summary>
    public async Task<JsonElement> FfprobeJsonAsync(
        string path,
        int timeoutSeconds = FfmpegCommands.FfprobeTimeoutSeconds,
        int probeSizeMb = FfmpegCommands.DefaultProbeSizeMb,
        int analyzeDurationSeconds = FfmpegCommands.DefaultAnalyzeDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var (ffprobe, _) = _resolver.Resolve();
        var argv = FfmpegCommands.BuildFfprobeArgv(ffprobe, path, probeSizeMb, analyzeDurationSeconds);
        var (exitCode, stdout, stderr) = await RunFfprobeAsync(path, argv, timeoutSeconds, cancellationToken).ConfigureAwait(false);
        return ProbeOutput.Interpret(exitCode, stdout, stderr);
    }

    /// <summary>
    /// #500: ffprobe's <c>-v warning</c> stderr for a file, as non-blank stripped lines. Best-effort: a timeout
    /// reads as no warnings rather than failing the whole validation over a diagnostic that could not be gathered.
    /// </summary>
    public async Task<IReadOnlyList<string>> ProbeWarningLinesAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var (ffprobe, _) = _resolver.Resolve();
        var argv = FfmpegCommands.BuildFfprobeWarningsArgv(ffprobe, path);
        var result = await _runner.RunAsync(
            new ProcessRequest
            {
                Argv = argv,
                Timeout = TimeSpan.FromSeconds(FfmpegCommands.FfprobeTimeoutSeconds),
                Stdin = ProcessInput.Inherit,
                Stdout = ProcessOutput.Discard,
                Stderr = ProcessOutput.Capture,
            },
            cancellationToken).ConfigureAwait(false);
        return result.TimedOut ? [] : RemuxOutputValidation.WarningLines(ProbeOutput.CapturedText(result.Stderr));
    }

    /// <summary>
    /// <see cref="FfprobeJsonAsync"/> and <see cref="ProbeWarningLinesAsync"/> together, from one ffprobe run where they
    /// can share it (#716). It throws as <see cref="FfprobeJsonAsync"/> does.
    /// </summary>
    /// <remarks>
    /// The warnings are always read with ffprobe's default probe window, the one a staged output is checked with, so a
    /// source's and an output's warnings compare like for like. When the probe asks for that window too, one run at
    /// <c>-v warning</c> gives both, because verbosity changes stderr only. A run that fails is repeated at
    /// <c>-v error</c>, so the failure is classified from error lines alone, as <see cref="FfprobeJsonAsync"/> would.
    /// </remarks>
    public async Task<ProbeWithWarnings> ProbeWithWarningsAsync(
        string path,
        int probeSizeMb = FfmpegCommands.DefaultProbeSizeMb,
        int analyzeDurationSeconds = FfmpegCommands.DefaultAnalyzeDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (probeSizeMb != FfmpegCommands.DefaultProbeSizeMb || analyzeDurationSeconds != FfmpegCommands.DefaultAnalyzeDurationSeconds)
        {
            var probe = await FfprobeJsonAsync(path, probeSizeMb: probeSizeMb, analyzeDurationSeconds: analyzeDurationSeconds, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return new ProbeWithWarnings(probe, await BaselineWarningsAsync(path, cancellationToken).ConfigureAwait(false));
        }

        var (ffprobe, _) = _resolver.Resolve();
        var argv = FfmpegCommands.BuildFfprobeWarningsArgv(ffprobe, path);
        var (exitCode, stdout, stderr) = await RunFfprobeAsync(path, argv, FfmpegCommands.FfprobeTimeoutSeconds, cancellationToken).ConfigureAwait(false);
        var warnings = RemuxOutputValidation.WarningLines(stderr);
        if (exitCode != 0)
        {
            return new ProbeWithWarnings(await FfprobeJsonAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false), warnings);
        }

        return new ProbeWithWarnings(ProbeOutput.Interpret(exitCode, stdout, stderr), warnings);
    }

    /// <summary>
    /// #500: a warnings baseline that cannot be read counts as "no known warnings", so a genuine new warning on the
    /// output still fails validation instead of being silently accepted.
    /// </summary>
    private async Task<IReadOnlyList<string>> BaselineWarningsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await ProbeWarningLinesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogBaselineWarningsUnreadable(exception, path);
            return [];
        }
    }

    /// <summary>One ffprobe run over a file that is there and not empty, logged as it goes. Throws on a timeout.</summary>
    private async Task<(int ExitCode, string Stdout, string Stderr)> RunFfprobeAsync(
        string path,
        IReadOnlyList<string> argv,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var (resolvedPath, exists, isFile, size, mtime) = _inspectFile(path);
        LogFfprobeFileState(ProbeOutput.FileStateLogPayload(path, resolvedPath, exists, isFile, size, MediaPathNames.Suffix(path, _windows), mtime));
        if (!exists || !isFile || size == 0)
        {
            throw new MediaToolException("file missing or empty at probe time");
        }

        LogFfprobeCall(ProbeOutput.CallLogPayload(path, argv));
        var result = await _runner.RunAsync(
            new ProcessRequest
            {
                Argv = argv,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                Stdin = ProcessInput.Inherit,
                Stdout = ProcessOutput.Capture,
                Stderr = ProcessOutput.Capture,
            },
            cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new MediaToolTimeoutException(ProbeOutput.TimeoutMessage(argv, timeoutSeconds));
        }

        var stdout = ProbeOutput.CapturedText(result.Stdout);
        var stderr = ProbeOutput.CapturedText(result.Stderr);
        var payload = ProbeOutput.ResultLogPayload(path, result.ExitCode, stdout, stderr);
        if (result.ExitCode != 0)
        {
            LogFfprobeResultWarning(payload);
        }
        else
        {
            LogFfprobeResultDebug(payload);
        }

        return (result.ExitCode, stdout, stderr);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "PROCESSING_FFPROBE_FILE_STATE: {Payload}")]
    private partial void LogFfprobeFileState(string payload);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PROCESSING_FFPROBE_CALL: {Payload}")]
    private partial void LogFfprobeCall(string payload);

    [LoggerMessage(Level = LogLevel.Debug, Message = "PROCESSING_FFPROBE_RESULT: {Payload}")]
    private partial void LogFfprobeResultDebug(string payload);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PROCESSING_FFPROBE_RESULT: {Payload}")]
    private partial void LogFfprobeResultWarning(string payload);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Weir could not read ffprobe's warnings for {Path}, so it compares the output against none.")]
    private partial void LogBaselineWarningsUnreadable(Exception error, string path);
}
