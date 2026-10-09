using System.Text;
using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.Updates;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// The tray's update files under <c>WEIR_HOME</c>: settings, the state the tray writes, the flags that ask it to check, download
/// or apply now, and the work state.
/// </summary>
public sealed class UpdateFiles
{
    public const string SettingsFileName = "update-settings.json";
    public const string StateFileName = "update-state.json";
    public const string ApplyFlagFileName = "update-apply-now";
    public const string CheckFlagFileName = "update-check-now";
    public const string DownloadFlagFileName = "update-download-now";

    /// <summary>What the tray reads to learn whether Weir is idle; written only while an update waits to install (#875).</summary>
    public const string WorkStateFileName = "work-state.json";

    private readonly WeirOptions _options;

    public UpdateFiles(WeirOptions options)
    {
        _options = options;
    }

    /// <summary>Read the update settings, with <paramref name="warn"/> called when the file is unreadable.</summary>
    public WireObject ReadSettings(Action<string>? warn = null)
    {
        var path = Path.Join(_options.WeirHome, SettingsFileName);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return UpdateStatus.DefaultUpdateSettings;
        }

        string text;
        try
        {
            text = File.ReadAllText(path, new UTF8Encoding(false, throwOnInvalidBytes: true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            warn?.Invoke(path);
            return UpdateStatus.UnreadableUpdateSettings;
        }

        if (text.StartsWith('﻿'))
        {
            warn?.Invoke(path);
            return UpdateStatus.UnreadableUpdateSettings;
        }

        var parsed = UpdateStatus.ParseUpdateSettings(text);
        if (parsed is null)
        {
            warn?.Invoke(path);
            return UpdateStatus.UnreadableUpdateSettings;
        }

        return parsed;
    }

    /// <summary>Save the update settings: written whole to a unique scratch file, then renamed into place.</summary>
    public WireObject WriteSettings(string mode, bool checkOnStartup, long checkIntervalMinutes)
    {
        var text = UpdateStatus.SerializeUpdateSettings(mode, checkOnStartup, checkIntervalMinutes);
        if (OperatingSystem.IsWindows())
        {
            // The file is written as Windows text, with CRLF newlines.
            text = text.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        ReplaceFile(SettingsFileName, Encoding.UTF8.GetBytes(text));
        return UpdateStatus.UpdateSettingsOut(mode, checkOnStartup, checkIntervalMinutes);
    }

    /// <summary>
    /// Read the update state the tray writes; a missing or unreadable file reads as the default state. A check or download that
    /// was asked for and not yet taken up by the tray reads as under way, so the person sees their click at once.
    /// </summary>
    public WireObject ReadState()
    {
        var written = ReadStateFile();
        if (written.Get("state") is not WireString { Value: UpdateStatus.StateIdle or UpdateStatus.StateFailed })
        {
            return written;
        }

        if (File.Exists(Path.Join(_options.WeirHome, CheckFlagFileName)))
        {
            return UpdateStatus.WithRequestedStep(written, UpdateStatus.StateChecking);
        }

        return File.Exists(Path.Join(_options.WeirHome, DownloadFlagFileName))
            ? UpdateStatus.WithRequestedStep(written, UpdateStatus.StateDownloading)
            : written;
    }

    private WireObject ReadStateFile()
    {
        var path = Path.Join(_options.WeirHome, StateFileName);
        if (!File.Exists(path))
        {
            return UpdateStatus.ParseUpdateState(null);
        }

        try
        {
            return UpdateStatus.ParseUpdateState(File.ReadAllText(path, new UTF8Encoding(false, throwOnInvalidBytes: true)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return UpdateStatus.ParseUpdateState(null);
        }
    }

    /// <summary>Ask the tray to restart and install the downloaded update.</summary>
    public void WriteApplyFlag() => WriteFlag(ApplyFlagFileName);

    /// <summary>Ask the tray to look for an update now.</summary>
    public void WriteCheckFlag() => WriteFlag(CheckFlagFileName);

    /// <summary>Ask the tray to download the available update now.</summary>
    public void WriteDownloadFlag() => WriteFlag(DownloadFlagFileName);

    /// <summary>Create a flag file, or refresh its modified time when it already exists.</summary>
    private void WriteFlag(string fileName)
    {
        var path = Path.Join(_options.WeirHome, fileName);
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return;
        }

        using (File.Create(path))
        {
        }
    }

    /// <summary>Whether the tray has downloaded an update that is waiting to be installed.</summary>
    public bool HasDownloadedUpdate() => ReadState().Get("downloaded")?.IsTruthy ?? false;

    /// <summary>Tell the tray whether file work is running or could start now, as of <paramref name="checkedAt"/>.</summary>
    public void WriteWorkState(bool busy, DateTimeOffset checkedAt) =>
        ReplaceFile(WorkStateFileName, Encoding.UTF8.GetBytes(UpdateStatus.SerializeWorkState(busy, checkedAt)));

    private void ReplaceFile(string fileName, byte[] contents) => AtomicFileWriter.Replace(_options.WeirHome, fileName, contents);

    /// <summary>The install type: <c>WEIR_RUNTIME</c>, a Docker marker, a packaged Windows build, else source.</summary>
    public static string DetectInstallType(string? runtimeVariable)
    {
        var runtime = (runtimeVariable ?? string.Empty).Trim().ToLowerInvariant();
        if (runtime is "windows" or "docker" or "source")
        {
            return runtime;
        }

        if (File.Exists("/.dockerenv") || Directory.Exists("/.dockerenv"))
        {
            return "docker";
        }

        // Only the packaged Windows build is published as a single file.
        var singleFile = string.IsNullOrEmpty(typeof(UpdateFiles).Assembly.Location);
        return singleFile && OperatingSystem.IsWindows() ? "windows" : "source";
    }
}
