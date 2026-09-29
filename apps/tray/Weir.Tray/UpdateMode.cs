using System.Text.Json.Serialization;

namespace Weir.Tray;

/// <summary>What the tray does when it finds an update, as chosen in update-settings.json.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
enum UpdateMode
{
    /// <summary>Download it, and install it when Weir next quits or starts, or when the person restarts to update.</summary>
    Auto,

    /// <summary>
    /// Download it and tell the person. It installs the same ways as in <see cref="Auto"/>; the difference is the
    /// notice, which offers the restart.
    /// </summary>
    DownloadOnly,

    /// <summary>Only say that it exists.</summary>
    NotifyOnly,
}
