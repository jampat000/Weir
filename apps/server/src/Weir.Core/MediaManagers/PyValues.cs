using System.Numerics;
using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>The small readers the media manager dialects share for loosely-typed JSON.</summary>
public static class PyValues
{
    /// <summary>A stripped non-empty string, or <see langword="null"/> for anything else.</summary>
    public static string? Text(PyJson? value)
    {
        if (value is not PyStr text)
        {
            return null;
        }

        var stripped = PyStrings.Strip(text.Value);
        return stripped.Length == 0 ? null : stripped;
    }

    /// <summary>An int, or a float with no fractional part; never a bool.</summary>
    public static BigInteger? WholeNumber(PyJson? value) => value switch
    {
        PyInt number => number.Value,
        PyFloat real when double.IsFinite(real.Value) && Math.Floor(real.Value) == real.Value => new BigInteger(real.Value),
        _ => null,
    };

    /// <summary>The value under <paramref name="key"/>, or null when absent.</summary>
    public static PyJson? Get(PyDict dict, string key)
    {
        ArgumentNullException.ThrowIfNull(dict);
        return dict.Get(key);
    }

    /// <summary>The first truthy value, else the last one.</summary>
    public static PyJson? Or(params PyJson?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        PyJson? last = null;
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
    public static string? FirstText(PyDict row, params string[] keys)
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
    public static BigInteger? FirstNumber(PyDict row, params string[] keys)
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
    public static List<PyDict> Dicts(PyJson? value) =>
        value is PyList list ? [.. list.Items.OfType<PyDict>()] : [];

    /// <summary>First character upper-cased, the rest lower-cased.</summary>
    public static string Capitalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == 0 ? value : value[..1].ToUpperInvariant() + value[1..].ToLowerInvariant();
    }

    public static PyJson Number(BigInteger? value) => value is { } number ? new PyInt(number) : PyNull.Instance;
}
