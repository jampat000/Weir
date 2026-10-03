using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace Weir.Core.Updates;

/// <summary>
/// A SemVer 2.0.0 version such as <c>1.0.0</c> or <c>1.0.0-rc.1</c>, ordered by SemVer precedence: a pre-release sorts before its
/// release, and numeric pre-release identifiers compare as numbers (<c>rc.10</c> is newer than <c>rc.9</c>). Build metadata
/// (<c>+build</c>) is read and ignored, as precedence ignores it.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private readonly BigInteger _major;
    private readonly BigInteger _minor;
    private readonly BigInteger _patch;
    private readonly IReadOnlyList<string> _preRelease;

    private SemanticVersion(BigInteger major, BigInteger minor, BigInteger patch, IReadOnlyList<string> preRelease)
    {
        _major = major;
        _minor = minor;
        _patch = patch;
        _preRelease = preRelease;
    }

    /// <summary>Whether this version has a pre-release part, as in <c>1.0.0-rc.1</c>.</summary>
    public bool IsPreRelease => _preRelease.Count > 0;

    /// <summary>Reads <c>1.2.3</c> or <c>1.2.3-rc.1</c>, with an optional leading <c>v</c>; false for anything else.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        var bare = text?.Trim() ?? string.Empty;
        if (bare.StartsWith('v'))
        {
            bare = bare[1..];
        }

        var plus = bare.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            bare = bare[..plus];
        }

        var hyphen = bare.IndexOf('-', StringComparison.Ordinal);
        var core = (hyphen < 0 ? bare : bare[..hyphen]).Split('.');
        if (core.Length != 3 || !core.All(IsNumber))
        {
            return false;
        }

        string[] preRelease = hyphen < 0 ? [] : bare[(hyphen + 1)..].Split('.');
        if (!preRelease.All(IsPreReleaseIdentifier))
        {
            return false;
        }

        version = new SemanticVersion(Number(core[0]), Number(core[1]), Number(core[2]), preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byNumbers = (_major, _minor, _patch).CompareTo((other._major, other._minor, other._patch));
        return byNumbers != 0 ? byNumbers : ComparePreRelease(_preRelease, other._preRelease);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);

    public override int GetHashCode() => HashCode.Combine(_major, _minor, _patch, string.Join('.', _preRelease));

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => left is not null && left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => !(left > right);

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => !(left < right);

    private static int ComparePreRelease(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return right.Count.CompareTo(left.Count);
        }

        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            var byIdentifier = CompareIdentifiers(left[i], right[i]);
            if (byIdentifier != 0)
            {
                return byIdentifier;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    private static int CompareIdentifiers(string left, string right) => (IsNumber(left), IsNumber(right)) switch
    {
        (true, true) => Number(left).CompareTo(Number(right)),
        (true, false) => -1,
        (false, true) => 1,
        _ => Math.Sign(string.CompareOrdinal(left, right)),
    };

    private static bool IsNumber(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0');

    private static bool IsPreReleaseIdentifier(string text) =>
        text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && (!text.All(char.IsAsciiDigit) || IsNumber(text));

    private static BigInteger Number(string digits) => BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
}
