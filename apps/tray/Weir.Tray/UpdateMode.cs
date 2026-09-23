using System.Text.Json.Serialization;

namespace Weir.Tray;

/// <summary>What the tray does when it finds an update, as chosen in update-settings.json.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
enum UpdateMode
{
    /// <summary>Download it and apply it when Weir next exits.</summary>
    Auto,

    /// <summary>Download it and wait for the person to restart.</summary>
    DownloadOnly,

    /// <summary>Only say that it exists.</summary>
    NotifyOnly,
}
