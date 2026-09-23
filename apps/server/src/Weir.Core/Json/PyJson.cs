using System.Globalization;
using System.Numerics;

namespace Weir.Core.Json;

/// <summary>
/// A JSON value with the distinctions the API and stored data depend on: integers of any size stay
/// integers, floats stay floats, and objects keep their key order (a later duplicate key replaces
/// the value but keeps the first key's position). Kept so API JSON stays byte-identical for existing
/// clients and data written by earlier releases reads back the same.
/// </summary>
public abstract class PyJson
{
    private protected PyJson()
    {
    }

    public static PyJson Null => PyNull.Instance;

    public static PyJson Of(string? value) => value is null ? PyNull.Instance : new PyStr(value);

    public static PyJson Of(bool value) => value ? PyBool.True : PyBool.False;

    public static PyJson Of(long value) => new PyInt(value);

    public static PyJson Of(long? value) => value is { } v ? new PyInt(v) : PyNull.Instance;

    public static PyJson Of(double value) => new PyFloat(value);

    /// <summary>Whether the value counts as set: false for null, <c>false</c>, zero, and empty strings, lists and objects.</summary>
    public abstract bool IsTruthy { get; }

    /// <summary>
    /// The type name used in error messages (<c>NoneType</c>, <c>int</c>, <c>dict</c> and so on), fixed so
    /// the error wording clients see stays the same.
    /// </summary>
    public abstract string PythonTypeName { get; }
}

public sealed class PyNull : PyJson
{
    public static readonly PyNull Instance = new();

    private PyNull()
    {
    }

    public override bool IsTruthy => false;

    public override string PythonTypeName => "NoneType";
}

public sealed class PyBool : PyJson
{
    public static readonly PyBool True = new(true);
    public static readonly PyBool False = new(false);

    private PyBool(bool value)
    {
        Value = value;
    }

    public bool Value { get; }

    public override bool IsTruthy => Value;

    public override string PythonTypeName => "bool";
}

public sealed class PyInt : PyJson
{
    public PyInt(BigInteger value)
    {
        Value = value;
    }

    public BigInteger Value { get; }

    public override bool IsTruthy => !Value.IsZero;

    public override string PythonTypeName => "int";

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed class PyFloat : PyJson
{
    public PyFloat(double value)
    {
        Value = value;
    }

    public double Value { get; }

    public override bool IsTruthy => Value != 0.0;

    public override string PythonTypeName => "float";
}

/// <summary>
/// A <c>datetime</c> held in memory (for example a column value read before it is serialized). It is not
/// a JSON type: convert it to text before writing.
/// </summary>
public sealed class PyDateTimeValue : PyJson
{
    public PyDateTimeValue(Time.PyDateTime value)
    {
        Value = value;
    }

    public Time.PyDateTime Value { get; }

    public override bool IsTruthy => true;

    public override string PythonTypeName => "datetime";
}

public sealed class PyStr : PyJson
{
    public PyStr(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }

    public override bool IsTruthy => Value.Length > 0;

    public override string PythonTypeName => "str";
}

public sealed class PyList : PyJson
{
    public PyList()
    {
        Items = [];
    }

    public PyList(IEnumerable<PyJson> items)
    {
        Items = [.. items];
    }

    public List<PyJson> Items { get; }

    public override bool IsTruthy => Items.Count > 0;

    public override string PythonTypeName => "list";
}

/// <summary>An insertion-ordered JSON object: setting an existing key replaces its value in place.</summary>
public sealed class PyDict : PyJson
{
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, PyJson> _values = new(StringComparer.Ordinal);

    public int Count => _keys.Count;

    public IReadOnlyList<string> Keys => _keys;

    public IEnumerable<KeyValuePair<string, PyJson>> Items => _keys.Select(key => new KeyValuePair<string, PyJson>(key, _values[key]));

    public override bool IsTruthy => _keys.Count > 0;

    public override string PythonTypeName => "dict";

    public PyJson this[string key]
    {
        get => _values[key];
        set => Set(key, value);
    }

    public PyDict Set(string key, PyJson value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!_values.ContainsKey(key))
        {
            _keys.Add(key);
        }

        _values[key] = value;
        return this;
    }

    public PyDict Set(string key, string? value) => Set(key, PyJson.Of(value));

    public PyDict Set(string key, bool value) => Set(key, PyJson.Of(value));

    public PyDict Set(string key, long value) => Set(key, PyJson.Of(value));

    public PyDict Set(string key, long? value) => Set(key, PyJson.Of(value));

    public PyDict Set(string key, double value) => Set(key, PyJson.Of(value));

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    /// <summary>Removes the key when present; returns whether it was.</summary>
    public bool Remove(string key)
    {
        if (!_values.Remove(key))
        {
            return false;
        }

        _keys.Remove(key);
        return true;
    }

    public bool TryGetValue(string key, out PyJson value)
    {
        if (_values.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = PyNull.Instance;
        return false;
    }

    /// <summary>The value for the key, or <see langword="null"/> when absent.</summary>
    public PyJson? Get(string key) => _values.TryGetValue(key, out var found) ? found : null;

    public PyDict Copy()
    {
        var copy = new PyDict();
        foreach (var key in _keys)
        {
            copy.Set(key, _values[key]);
        }

        return copy;
    }
}
