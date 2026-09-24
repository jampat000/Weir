using System.Globalization;
using System.Numerics;

namespace Weir.Core.Json;

/// <summary>
/// A JSON value with the distinctions the API and stored data depend on: integers of any size stay
/// integers, floats stay floats, and objects keep their key order (a later duplicate key replaces
/// the value but keeps the first key's position). Kept so API JSON stays byte-identical for existing
/// clients and data written by earlier releases reads back the same.
/// </summary>
public abstract class WireValue
{
    private protected WireValue()
    {
    }

    public static WireValue Null => WireNull.Instance;

    public static WireValue Of(string? value) => value is null ? WireNull.Instance : new WireString(value);

    public static WireValue Of(bool value) => value ? WireBool.True : WireBool.False;

    public static WireValue Of(long value) => new WireInteger(value);

    public static WireValue Of(long? value) => value is { } v ? new WireInteger(v) : WireNull.Instance;

    public static WireValue Of(double value) => new WireNumber(value);

    /// <summary>Whether the value counts as set: false for null, <c>false</c>, zero, and empty strings, lists and objects.</summary>
    public abstract bool IsTruthy { get; }

    /// <summary>
    /// The type name used in error messages (<c>NoneType</c>, <c>int</c>, <c>dict</c> and so on), fixed so
    /// the error wording clients see stays the same.
    /// </summary>
    public abstract string WireTypeName { get; }
}

public sealed class WireNull : WireValue
{
    public static readonly WireNull Instance = new();

    private WireNull()
    {
    }

    public override bool IsTruthy => false;

    public override string WireTypeName => "NoneType";
}

public sealed class WireBool : WireValue
{
    public static readonly WireBool True = new(true);
    public static readonly WireBool False = new(false);

    private WireBool(bool value)
    {
        Value = value;
    }

    public bool Value { get; }

    public override bool IsTruthy => Value;

    public override string WireTypeName => "bool";
}

public sealed class WireInteger : WireValue
{
    public WireInteger(BigInteger value)
    {
        Value = value;
    }

    public BigInteger Value { get; }

    public override bool IsTruthy => !Value.IsZero;

    public override string WireTypeName => "int";

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed class WireNumber : WireValue
{
    public WireNumber(double value)
    {
        Value = value;
    }

    public double Value { get; }

    public override bool IsTruthy => Value != 0.0;

    public override string WireTypeName => "float";
}

/// <summary>
/// A <c>datetime</c> held in memory (for example a column value read before it is serialized). It is not
/// a JSON type: convert it to text before writing.
/// </summary>
public sealed class WireTimestampValue : WireValue
{
    public WireTimestampValue(Time.Timestamp value)
    {
        Value = value;
    }

    public Time.Timestamp Value { get; }

    public override bool IsTruthy => true;

    public override string WireTypeName => "datetime";
}

public sealed class WireString : WireValue
{
    public WireString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }

    public override bool IsTruthy => Value.Length > 0;

    public override string WireTypeName => "str";
}

public sealed class WireArray : WireValue
{
    public WireArray()
    {
        Items = [];
    }

    public WireArray(IEnumerable<WireValue> items)
    {
        Items = [.. items];
    }

    public List<WireValue> Items { get; }

    public override bool IsTruthy => Items.Count > 0;

    public override string WireTypeName => "list";
}

/// <summary>An insertion-ordered JSON object: setting an existing key replaces its value in place.</summary>
public sealed class WireObject : WireValue
{
    private readonly List<string> _keys = [];
    private readonly Dictionary<string, WireValue> _values = new(StringComparer.Ordinal);

    public int Count => _keys.Count;

    public IReadOnlyList<string> Keys => _keys;

    public IEnumerable<KeyValuePair<string, WireValue>> Items => _keys.Select(key => new KeyValuePair<string, WireValue>(key, _values[key]));

    public override bool IsTruthy => _keys.Count > 0;

    public override string WireTypeName => "dict";

    public WireValue this[string key]
    {
        get => _values[key];
        set => Set(key, value);
    }

    public WireObject Set(string key, WireValue value)
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

    public WireObject Set(string key, string? value) => Set(key, WireValue.Of(value));

    public WireObject Set(string key, bool value) => Set(key, WireValue.Of(value));

    public WireObject Set(string key, long value) => Set(key, WireValue.Of(value));

    public WireObject Set(string key, long? value) => Set(key, WireValue.Of(value));

    public WireObject Set(string key, double value) => Set(key, WireValue.Of(value));

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

    public bool TryGetValue(string key, out WireValue value)
    {
        if (_values.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = WireNull.Instance;
        return false;
    }

    /// <summary>The value for the key, or <see langword="null"/> when absent.</summary>
    public WireValue? Get(string key) => _values.TryGetValue(key, out var found) ? found : null;

    public WireObject Copy()
    {
        var copy = new WireObject();
        foreach (var key in _keys)
        {
            copy.Set(key, _values[key]);
        }

        return copy;
    }
}
