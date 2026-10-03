using System.Buffers.Text;
using System.Text;
using Weir.Core.Json;

namespace Weir.Core.Paging;

/// <summary>
/// Where the next page of a sorted list starts, as the opaque text a client hands back. It holds the key of the last row of
/// the page, and the sort and direction it was made under, so a cursor can only continue the list that gave it out.
/// </summary>
public static class KeysetCursor
{
    /// <summary>The longest cursor read: a key is a few short values, so anything longer was not made here.</summary>
    private const int MaxLength = 2048;

    private const string SortField = "sort";
    private const string DirectionField = "direction";
    private const string AfterField = "after";

    public static string Encode(string sort, SortDirection direction, IReadOnlyList<object?> key)
    {
        ArgumentNullException.ThrowIfNull(sort);
        ArgumentNullException.ThrowIfNull(key);
        var wire = new WireObject()
            .Set(SortField, sort)
            .Set(DirectionField, SortDirections.NameOf(direction))
            .Set(AfterField, new WireArray(key.Select(ToWire)));
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(WireJsonWriter.Dumps(wire, WireJsonFormat.Compact)));
    }

    /// <summary>
    /// The key a cursor stands for, or false when the text is not a cursor this server wrote for <paramref name="sort"/> in
    /// <paramref name="direction"/>, or its values are not the ones <paramref name="parts"/> name.
    /// </summary>
    public static bool TryDecode(string? text, string sort, SortDirection direction, IReadOnlyList<KeysetPart> parts, out IReadOnlyList<object?> key)
    {
        ArgumentNullException.ThrowIfNull(parts);
        key = [];
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || !Base64Url.IsValid(text))
        {
            return false;
        }

        WireValue parsed;
        try
        {
            parsed = WireJsonParser.Parse(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text)));
        }
        catch (WireJsonDecodeException)
        {
            return false;
        }

        if (parsed is not WireObject wire
            || !wire.TryGetValue(SortField, out var wireSort) || wireSort is not WireString { Value: var madeFor } || madeFor != sort
            || !wire.TryGetValue(DirectionField, out var wireDirection) || wireDirection is not WireString { Value: var way } || way != SortDirections.NameOf(direction)
            || !wire.TryGetValue(AfterField, out var wireAfter) || wireAfter is not WireArray { Items: var items } || items.Count != parts.Count)
        {
            return false;
        }

        var values = new List<object?>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            if (!TryRead(items[index], parts[index].Kind, out var value))
            {
                return false;
            }

            values.Add(value);
        }

        key = values;
        return true;
    }

    private static WireValue ToWire(object? value) => value switch
    {
        null => WireValue.Null,
        long number => WireValue.Of(number),
        string text => WireValue.Of(text),
        _ => throw new ArgumentException("A cursor holds whole numbers and text.", nameof(value)),
    };

    private static bool TryRead(WireValue wire, KeysetValueKind kind, out object? value)
    {
        value = null;
        switch (wire)
        {
            case WireNull:
                return true;
            case WireInteger { Value: var number } when kind == KeysetValueKind.Number && number >= long.MinValue && number <= long.MaxValue:
                value = (long)number;
                return true;
            case WireString { Value: var text } when kind != KeysetValueKind.Number:
                value = text;
                return true;
            default:
                return false;
        }
    }
}
