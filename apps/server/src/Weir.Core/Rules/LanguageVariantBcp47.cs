namespace Weir.Core.Rules;

/// <summary>Reads a regional/script variant directly from an explicit BCP 47 tag, ahead of any name-based detection.</summary>
public static partial class LanguageVariants
{
    /// <summary>
    /// Reads a BCP 47 tag directly (<c>fr-CA</c>, <c>pt-BR</c>, <c>zh-Hant</c>), or a bare tag that
    /// is itself one of the table's alias identifiers (<c>yue</c>, <c>cmn</c>). Only a region or
    /// script subtag this table actually knows about is recognized; anything else falls through to
    /// name-based detection.
    /// </summary>
    private static VariantDetection FromBcp47(string? rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag))
        {
            return VariantDetection.None;
        }

        var parts = rawTag.Trim().Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return VariantDetection.None;
        }

        var primary = parts[0];
        if (string.Equals(primary, "yue", StringComparison.OrdinalIgnoreCase) || string.Equals(primary, "cmn", StringComparison.OrdinalIgnoreCase))
        {
            return new VariantDetection(primary.ToLowerInvariant(), VariantSource.Tag, rawTag);
        }

        if (!Bcp47Primary.TryGetValue(primary, out var mapped))
        {
            return VariantDetection.None;
        }

        for (var i = 1; i < parts.Length; i++)
        {
            var subtag = parts[i];
            string? candidate = null;
            if (subtag.Length == 4 && subtag.All(char.IsLetter))
            {
                candidate = $"{mapped.CanonicalBase}-{char.ToUpperInvariant(subtag[0])}{subtag[1..].ToLowerInvariant()}";
            }
            else if (subtag.Length == 2 && subtag.All(char.IsLetter))
            {
                candidate = $"{mapped.CanonicalBase}-{subtag.ToUpperInvariant()}";
            }
            else if (subtag.Length == 3 && subtag.All(char.IsDigit))
            {
                candidate = $"{mapped.CanonicalBase}-{subtag}";
            }

            if (candidate is not null && KnownIdentifiers.Contains(candidate))
            {
                return new VariantDetection(candidate, VariantSource.Tag, rawTag);
            }
        }

        return VariantDetection.None;
    }
}
