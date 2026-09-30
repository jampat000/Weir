using System.Security.Cryptography;
using System.Text;
using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.Updates;

namespace Weir.Infrastructure.Runtime;

/// <summary>The tray's update files under <c>WEIR_HOME</c>: settings, state, the apply-now flag and the work state.</summary>
public sealed class UpdateFiles
{
    public const string SettingsFileName = "update-settings.json";
    public const string StateFileName = "update-state.json";
    public const string ApplyFlagFileName = "update-apply-now";

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

    /// <summary>Read the update state the tray writes; a missing or unreadable file reads as the default state.</summary>
    public WireObject ReadState()
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

    /// <summary>Create the apply-now flag file, or refresh its modified time when it already exists.</summary>
    public void WriteApplyFlag()
    {
        var path = Path.Join(_options.WeirHome, ApplyFlagFileName);
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

    // Written whole to a unique scratch file, then renamed into place, so a reader sees the old file or the new one, never half.
    private void ReplaceFile(string fileName, byte[] contents)
    {
        var path = Path.Join(_options.WeirHome, fileName);
        var scratch = Path.Join(_options.WeirHome, $".{fileName}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.tmp");
        try
        {
            File.WriteAllBytes(scratch, contents);
            File.Move(scratch, path, overwrite: true);
            scratch = string.Empty;
        }
        finally
        {
            if (scratch.Length > 0 && File.Exists(scratch))
            {
                File.Delete(scratch);
            }
        }
    }

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
