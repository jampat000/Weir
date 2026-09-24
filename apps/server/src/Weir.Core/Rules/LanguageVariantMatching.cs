namespace Weir.Core.Rules;

/// <summary>Matching a configured language/variant rule against a track, and normalizing a configured value the same way.</summary>
public static partial class LanguageVariants
{
    /// <summary>
    /// A plain base-language rule (<c>"fre"</c>) matches every track of that base language,
    /// variant or not — existing behaviour, unchanged. A variant rule (<c>"fre-CA"</c>) matches
    /// only a track detected as that exact variant.
    /// </summary>
    public static bool Matches(string configuredLangOrVariant, string trackBaseLang, string? trackVariant)
    {
        if (string.IsNullOrEmpty(configuredLangOrVariant))
        {
            return false;
        }

        if (IsVariantIdentifier(configuredLangOrVariant))
        {
            return trackVariant is not null && string.Equals(trackVariant, configuredLangOrVariant, StringComparison.Ordinal);
        }

        return string.Equals(trackBaseLang, configuredLangOrVariant, StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a configured audio-preference or subtitle-language value the same way a track's
    /// own tag is (<see cref="RemuxRules.NormalizeLang"/>), except that a recognized variant
    /// identifier — spelled exactly (<c>"fre-CA"</c>, any case) or as a BCP 47 tag (<c>"fr-CA"</c>)
    /// — is preserved instead of being reduced to its base language.
    /// </summary>
    public static string NormalizeLanguageOrVariant(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        foreach (var id in KnownIdentifiers)
        {
            if (string.Equals(trimmed, id, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        var viaTag = FromBcp47(trimmed);
        if (viaTag.Found)
        {
            return viaTag.Identifier!;
        }

        return RemuxRules.NormalizeLang(raw);
    }
}
