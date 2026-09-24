using System.Numerics;
using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>The small readers the media manager dialects share for loosely-typed JSON.</summary>
public static class ManagerValues
{
    /// <summary>A stripped non-empty string, or <see langword="null"/> for anything else.</summary>
    public static string? Text(WireValue? value)
    {
        if (value is not WireString text)
        {
            return null;
        }

        var stripped = WireStrings.Strip(text.Value);
        return stripped.Length == 0 ? null : stripped;
    }

    /// <summary>An int, or a float with no fractional part; never a bool.</summary>
    public static BigInteger? WholeNumber(WireValue? value) => value switch
    {
        WireInteger number => number.Value,
        WireNumber real when double.IsFinite(real.Value) && Math.Floor(real.Value) == real.Value => new BigInteger(real.Value),
        _ => null,
    };

    /// <summary>The value under <paramref name="key"/>, or null when absent.</summary>
    public static WireValue? Get(WireObject dict, string key)
    {
        ArgumentNullException.ThrowIfNull(dict);
        return dict.Get(key);
    }

    /// <summary>The first truthy value, else the last one.</summary>
    public static WireValue? Or(params WireValue?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        WireValue? last = null;
        foreach (var value in values)
        {
            last = value;
            if (value is not null && value.IsTruthy)
            {
                return value;
            }
        }

        return last;
    }

    /// <summary>The first of <paramref name="keys"/> holding <see cref="Text"/>, or null.</summary>
    public static string? FirstText(WireObject row, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(row);
        foreach (var key in keys)
        {
            if (Text(row.Get(key)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The first of <paramref name="keys"/> holding a <see cref="WholeNumber"/>, or null.</summary>
    public static BigInteger? FirstNumber(WireObject row, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(row);
        foreach (var key in keys)
        {
            if (WholeNumber(row.Get(key)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>The objects in a list, or nothing.</summary>
    public static List<WireObject> Dicts(WireValue? value) =>
        value is WireArray list ? [.. list.Items.OfType<WireObject>()] : [];

    /// <summary>First character upper-cased, the rest lower-cased.</summary>
    public static string Capitalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == 0 ? value : value[..1].ToUpperInvariant() + value[1..].ToLowerInvariant();
    }

    public static WireValue Number(BigInteger? value) => value is { } number ? new WireInteger(number) : WireNull.Instance;
}
