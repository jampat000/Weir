using System.Text;
using System.Text.Json;
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

    /// <summary>The tray's record of an update it held back for want of a copy of Weir's data: <c>{ "version": …, "reason": … }</c>.</summary>
    public const string NotAppliedFileName = "update-not-applied.json";

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
    /// Read the update state the tray writes; a missing or unreadable file reads as the default state. While the tray holds an
    /// update back because it could not save a copy of Weir's data first (<see cref="NotAppliedFileName"/>), the state also says why.
    /// </summary>
    public WireObject ReadState()
    {
        var state = ParseState();
        if (ReadNotAppliedReason() is { } reason)
        {
            state.Set("not_updated_reason", reason);
        }

        return state;
    }

    private WireObject ParseState()
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

    private string? ReadNotAppliedReason()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Join(_options.WeirHome, NotAppliedFileName)));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("reason", out var reason)
                && reason.ValueKind == JsonValueKind.String
                && reason.GetString() is { Length: > 0 } text
                    ? text
                    : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
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
