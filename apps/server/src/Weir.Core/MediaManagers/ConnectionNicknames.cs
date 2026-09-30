using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// The optional short nickname a person can give a media manager or download client connection, shown after the
/// name Weir derives from where it runs: "Radarr on nas · 4K".
/// </summary>
public static class ConnectionNicknames
{
    public const int MaxLength = 30;

    /// <summary>What separates a connection's name from its nickname wherever both are shown.</summary>
    public const string Separator = " · ";

    /// <summary>The nickname without surrounding whitespace, or <see langword="null"/> when it is blank.</summary>
    public static string? Normalize(string? raw)
    {
        var trimmed = WireStrings.Strip(raw ?? string.Empty);
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Whether an update asking for <paramref name="requested"/> changes the <paramref name="current"/> nickname, and the
    /// nickname to save when it does. A request of <see langword="null"/> leaves the nickname alone; a blank one clears it.
    /// </summary>
    public static bool TryChange(string? current, string? requested, out string? wanted)
    {
        wanted = Normalize(requested);
        return requested is not null && wanted != current;
    }

    /// <summary>Whether a nickname, as <see cref="Normalize"/> leaves it, is short enough to keep.</summary>
    public static bool Fits(string? normalized) => normalized is null || normalized.Length <= MaxLength;
}
