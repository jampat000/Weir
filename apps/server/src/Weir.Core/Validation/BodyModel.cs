using System.Numerics;
using Weir.Core.Json;

namespace Weir.Core.Validation;

/// <summary>How a request model treats keys it does not declare.</summary>
public enum ExtraFields
{
    Ignore,
    Forbid,
}

/// <summary>
/// Validates one JSON body against a request model, field by field in declaration order.
/// Create it, read each field once, then call <see cref="Finish"/>.
/// </summary>
public sealed class BodyModel
{
    private static readonly object[] BodyLoc = ["body"];

    private readonly WireObject? _dict;
    private readonly ValidationIssues _issues;
    private readonly HashSet<string> _declared = new(StringComparer.Ordinal);
    private bool _valid = true;

    /// <param name="body">The parsed body, or <see langword="null"/> when the request had none.</param>
    /// <param name="issues">Where errors are collected.</param>
    public BodyModel(WireValue? body, ValidationIssues issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        _issues = issues;
        if (body is null or WireNull)
        {
            issues.Add(FieldRules.Missing(BodyLoc, WireNull.Instance));
            _valid = false;
            IsPresent = false;
        }
        else if (body is WireObject dict)
        {
            _dict = dict;
            IsPresent = true;
        }
        else
        {
            issues.Add(new ValidationIssue("model_attributes_type", BodyLoc, "Input should be a valid dictionary or object to extract fields from", body));
            _valid = false;
            IsPresent = true;
        }
    }

    /// <summary>A body was sent (even if it is not an object).</summary>
    public bool IsPresent { get; }

    /// <summary>Every field read so far validated.</summary>
    public bool IsValid => _valid;

    public string Str(string name, int? minLength = null, int? maxLength = null) =>
        Read(name, required: true, input => FieldRules.TryStr(input, Loc(name), minLength, maxLength, _issues, out var v) ? v : null) ?? string.Empty;

    public string? OptionalStr(string name, string? defaultValue = null, int? minLength = null, int? maxLength = null) =>
        ReadNullable(name, defaultValue, input => FieldRules.TryStr(input, Loc(name), minLength, maxLength, _issues, out var v) ? (true, v) : (false, null));

    public long Number(string name, long defaultValue, bool required, long? ge = null, long? le = null)
    {
        var result = Read<long?>(name, required, input => FieldRules.TryInt(input, Loc(name), ge, le, _issues, out var v) ? Saturate(v) : null);
        return result ?? defaultValue;
    }

    public long? OptionalInt(string name, long? ge = null, long? le = null) =>
        ReadNullableStruct<long>(name, input => FieldRules.TryInt(input, Loc(name), ge, le, _issues, out var v) ? (true, Saturate(v)) : (false, 0));

    public bool Bool(string name, bool defaultValue, bool required = false)
    {
        var result = Read<bool?>(name, required, input => FieldRules.TryBool(input, Loc(name), _issues, out var v) ? v : null);
        return result ?? defaultValue;
    }

    public bool? OptionalBool(string name) =>
        ReadNullableStruct<bool>(name, input => FieldRules.TryBool(input, Loc(name), _issues, out var v) ? (true, v) : (false, false));

    public string Literal(string name, IReadOnlyList<string> allowed, string? defaultValue = null) =>
        Read(name, required: defaultValue is null, input => FieldRules.TryLiteral(input, Loc(name), allowed, _issues, out var v) ? v : null) ?? defaultValue ?? string.Empty;

    public List<string> StrList(string name, IReadOnlyList<string> defaultValue) =>
        Read(name, required: false, input => FieldRules.TryStrList(input, Loc(name), _issues, out var v) ? v : null) ?? [.. defaultValue];

    public WireObject Dict(string name) =>
        Read(name, required: true, input => FieldRules.TryDict(input, Loc(name), _issues, out var v) ? v : null) ?? new WireObject();

    /// <summary>Like <see cref="Dict"/>, but a missing key is not an error: it returns <see langword="null"/> so the caller can fall back to defaults.</summary>
    public WireObject? OptionalDict(string name) =>
        Read<WireObject>(name, required: false, input => FieldRules.TryDict(input, Loc(name), _issues, out var v) ? v : null);

    public List<long> IntList(string name, IReadOnlyList<long>? defaultValue = null) =>
        Read(name, required: false, input => FieldRules.TryIntList(input, Loc(name), _issues, out var v) ? v : null) ?? [.. defaultValue ?? []];

    public Time.Timestamp? OptionalDateTime(string name) =>
        ReadNullableStruct<Time.Timestamp>(name, input =>
        {
            var ok = FieldRules.TryDateTime(input, Loc(name), _issues, out var v);
            return (ok, v ?? default);
        });

    /// <summary>Report undeclared keys when the model forbids them.</summary>
    public void Finish(ExtraFields extra)
    {
        if (_dict is null || extra == ExtraFields.Ignore)
        {
            return;
        }

        foreach (var (key, value) in _dict.Items)
        {
            if (!_declared.Contains(key))
            {
                _issues.Add(new ValidationIssue("extra_forbidden", Loc(key), "Extra inputs are not permitted", value));
                _valid = false;
            }
        }
    }

    private static object[] Loc(string name) => ["body", name];

    private static long Saturate(BigInteger value) =>
        value > long.MaxValue ? long.MaxValue : value < long.MinValue ? long.MinValue : (long)value;

    private T? Read<T>(string name, bool required, Func<WireValue, T?> validate)
    {
        _declared.Add(name);
        if (_dict is null)
        {
            return default;
        }

        if (!_dict.TryGetValue(name, out var input))
        {
            if (required)
            {
                _issues.Add(FieldRules.Missing(Loc(name), _dict));
                _valid = false;
            }

            return default;
        }

        var result = validate(input);
        if (result is null)
        {
            _valid = false;
        }

        return result;
    }

    private string? ReadNullable(string name, string? defaultValue, Func<WireValue, (bool Ok, string? Value)> validate)
    {
        _declared.Add(name);
        if (_dict is null || !_dict.TryGetValue(name, out var input))
        {
            return defaultValue;
        }

        if (input is WireNull)
        {
            return null;
        }

        var (ok, value) = validate(input);
        _valid &= ok;
        return value;
    }

    private T? ReadNullableStruct<T>(string name, Func<WireValue, (bool Ok, T Value)> validate)
        where T : struct
    {
        _declared.Add(name);
        if (_dict is null || !_dict.TryGetValue(name, out var input) || input is WireNull)
        {
            return null;
        }

        var (ok, value) = validate(input);
        _valid &= ok;
        return ok ? value : null;
    }
}
