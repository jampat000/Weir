using System.Text.Json.Serialization;

namespace Weir.Tray;

/// <summary>What the tray does when it finds an update, as chosen in update-settings.json.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
enum UpdateMode
{
    /// <summary>
    /// Download it, and install it once Weir has been idle for a while (<see cref="IdleInstall"/>), when Weir next
    /// quits or starts, or when the person restarts to update.
    /// </summary>
    Auto,

    /// <summary>
    /// Download it and tell the person, in a notice that offers the restart. It installs when the person restarts to
    /// update, or when Weir next quits or starts; Weir never restarts by itself to install it.
    /// </summary>
    DownloadOnly,

    /// <summary>Only say that it exists.</summary>
    NotifyOnly,
}
