namespace Weir.Core.Rules;

/// <summary>Where a detected <see cref="VariantDetection"/> came from.</summary>
public enum VariantSource
{
    /// <summary>No variant was detected (or the identifier is just an alias of the base, not detected).</summary>
    None,

    /// <summary>An explicit BCP 47 region or script subtag on the track's own language tag.</summary>
    Tag,

    /// <summary>A marker word or phrase in the track's name (<c>tags.title</c>).</summary>
    Name,
}

/// <summary>
/// One outcome of <see cref="LanguageVariants.Detect"/>: the identifier (if any), where it came
/// from, and the exact marker text for a plan note (e.g. <c>"French (Canada), from the track name
/// 'VFQ'."</c>).
/// </summary>
public sealed record VariantDetection(string? Identifier, VariantSource Source, string? Marker)
{
    public static readonly VariantDetection None = new(null, VariantSource.None, null);

    public bool Found => Identifier is { Length: > 0 };
}
