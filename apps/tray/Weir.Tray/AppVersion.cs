using System.Reflection;

namespace Weir.Tray;

/// <summary>The version this tray was built as, the one the release is tagged with (<c>1.0.0-rc.10</c>).</summary>
static class AppVersion
{
    /// <summary>The version without the build metadata (<c>+abc123</c>) that follows it, which is not part of the release.</summary>
    internal static string Current { get; } = Without(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string Without(string? informationalVersion)
    {
        var version = (informationalVersion ?? string.Empty).Split('+')[0].Trim();
        return version.Length > 0 ? version : "unknown";
    }
}
