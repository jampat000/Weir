using System.Text.Json;

namespace Weir.Tray;

/// <summary>The update choices in update-settings.json, which the tray and the server's Settings page both write.</summary>
sealed class UpdateSettings
{
    internal const string FileName = "update-settings.json";
    internal const int DefaultCheckIntervalMinutes = 60;

    public UpdateMode Mode { get; set; } = UpdateMode.Auto;
    public bool CheckOnStartup { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = DefaultCheckIntervalMinutes;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal static UpdateSettings Load(string runtimeHome)
    {
        var path = Path.Combine(runtimeHome, FileName);
        // No file is a fresh install: nobody has chosen yet, so the shipped default applies.
        if (!File.Exists(path))
        {
            return new UpdateSettings();
        }
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UpdateSettings>(json, JsonOptions)
                ?? throw new InvalidDataException($"{FileName} parsed as null.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
        {
            // A file that exists but cannot be read means a choice was made and we cannot tell what it was. Auto
            // is the only mode that installs an update with nobody watching, so it is the one guess that can act
            // against an explicit choice: an operator who picked NotifyOnly would be auto-updated by a truncated
            // file. This read decides whether to install, so it falls back to the mode that installs nothing.
            TrayLog.Write(
                $"{FileName} could not be read ({ex.Message}). Using NotifyOnly so a damaged "
                + "file cannot install an update the operator did not choose.");
            return new UpdateSettings { Mode = UpdateMode.NotifyOnly };
        }
    }

    // The server writes this same file when the Settings page saves; AtomicFile keeps the two writers from
    // renaming each other's scratch file into place, and keeps Load's damaged-file fallback unreachable in practice.
    internal void Save(string runtimeHome) =>
        AtomicFile.WriteAllText(runtimeHome, FileName, JsonSerializer.Serialize(this, JsonOptions));
}
