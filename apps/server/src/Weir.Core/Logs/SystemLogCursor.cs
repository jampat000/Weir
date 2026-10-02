using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Weir.Core.Logs;

/// <summary>
/// Where the next page of the log starts, as the opaque text a client hands back. It holds the last row's
/// <see cref="SystemLogPosition"/>, so paging never skips or repeats a row that shares its time with the page boundary.
/// </summary>
public static class SystemLogCursor
{
    private const char Separator = '.';

    public static string Encode(SystemLogPosition position) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{position.At.UtcTicks}{Separator}{(int)position.Source}{Separator}{position.Key}")));

    /// <summary>The position a cursor stands for, or false when the text is not one this server wrote.</summary>
    public static bool TryDecode(string? text, out SystemLogPosition position)
    {
        position = default;
        if (string.IsNullOrEmpty(text) || !Base64Url.IsValid(text))
        {
            return false;
        }

        var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(text)).Split(Separator);
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var source)
            || !Enum.IsDefined((SystemLogSource)source)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var key)
            || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        position = new SystemLogPosition(new DateTimeOffset(ticks, TimeSpan.Zero), (SystemLogSource)source, key);
        return true;
    }
}
