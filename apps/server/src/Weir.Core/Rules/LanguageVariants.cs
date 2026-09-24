namespace Weir.Core.Rules;

/// <summary>
/// Issue #496: tells regional language variants apart (Quebec vs France French, Latin American vs
/// Castilian Spanish, Brazilian vs European Portuguese, Traditional vs Simplified Chinese,
/// Cantonese vs Mandarin, Flemish) from a track's name or an explicit BCP 47 region/script subtag.
/// Derived from Muxarr's <c>Muxarr.Core/Language/LanguageVariants.cs</c> (https://github.com/KirovAir/muxarr,
/// GPL-3.0); see THIRD_PARTY_NOTICES.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identifier scheme.</b> An identifier is either <c>{base}-{REGION}</c> (an ISO 3166-1
/// alpha-2 country code, e.g. <c>fre-CA</c>, <c>por-BR</c>), <c>{base}-{region}</c> using a UN M49
/// numeric region for a group of countries (<c>spa-419</c>, Latin America and the Caribbean, as
/// CLDR spells <c>es-419</c>), <c>{base}-{Script}</c> using an ISO 15924 script subtag in
/// title case (<c>zho-Hant</c>, <c>zho-Hans</c>), or a bare ISO 639-3 code for a language that is
/// its own code but is offered as a refinement of a macrolanguage tag (<c>yue</c> Cantonese,
/// <c>cmn</c> Mandarin, both refinements of a <c>chi</c>/<c>zho</c> track). <c>base</c> is a fixed,
/// canonical spelling per entry (chosen to match the issue's own examples: the bibliographic form
/// for French, the terminological form for Chinese and Dutch), not necessarily the exact spelling
/// of the input tag — a track tagged <c>fra</c>, <c>fre</c> or <c>fr</c> all refine to <c>fre-CA</c>.
/// </para>
/// <para>
/// <b>Matching a marker.</b> A marker is either a short, ambiguous token (<c>VFQ</c>, <c>CHT</c>,
/// <c>pt-BR</c>) matched as a whole token — case-insensitive, accent-insensitive (diacritics are
/// stripped from both the marker and the title before comparing, so "Québécois" and "Quebecois"
/// are the same token), with <c>.</c> and <c>_</c> treated as word separators like a space — or a
/// marker containing CJK characters (<c>繁體</c>, <c>粵語</c>), matched as a plain substring since
/// CJK text has no spacing between words.
/// </para>
/// <para>
/// <b>Refine-only.</b> Each variant's markers are split into <i>self-identifying</i> (imply both
/// the base language and the variant on their own, e.g. <c>VFQ</c>) and <i>refine-only</i> (only
/// mean anything once the base language is already known, e.g. bare <c>Latino</c>, which could
/// belong to any number of unrelated words). A track's base language classifies as: undetermined
/// (empty or <c>und</c>) — only self-identifying markers apply; the same family as the variant
/// (e.g. base <c>fre</c> for a French variant) — both self-identifying and refine-only markers
/// apply; or conflicting (e.g. base <c>eng</c> for a French variant) — no marker for that variant
/// applies, self-identifying included, since it contradicts a language the track already has. An
/// explicit BCP 47 region/script subtag is read directly and never overridden by a name marker.
/// </para>
/// </remarks>
public static partial class LanguageVariants
{
    // --- the variant table --------------------------------------------------------------

    private static readonly string[] FrenchFamily = ["fre", "fra", "fr"];
    private static readonly string[] SpanishFamily = ["spa", "es"];
    private static readonly string[] PortugueseFamily = ["por", "pt"];
    private static readonly string[] ChineseFamily = ["chi", "zho", "zh"];
    private static readonly string[] DutchFamily = ["dut", "nld", "nl"];

    // Kept in this file, alongside the family arrays above: a field initializer elsewhere in this
    // partial class that read them before this file's initializers ran would see them as
    // uninitialized, since partial-class field initializers run in file order.
    private static readonly Dictionary<string, (string CanonicalBase, string[] Family)> Bcp47Primary =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["fr"] = ("fre", FrenchFamily),
            ["fre"] = ("fre", FrenchFamily),
            ["fra"] = ("fre", FrenchFamily),
            ["es"] = ("spa", SpanishFamily),
            ["spa"] = ("spa", SpanishFamily),
            ["pt"] = ("por", PortugueseFamily),
            ["por"] = ("por", PortugueseFamily),
            ["zh"] = ("zho", ChineseFamily),
            ["chi"] = ("zho", ChineseFamily),
            ["zho"] = ("zho", ChineseFamily),
            ["nl"] = ("nld", DutchFamily),
            ["dut"] = ("nld", DutchFamily),
            ["nld"] = ("nld", DutchFamily),
        };

    private sealed record Marker(string Display, string Normalized)
    {
        public static Marker Of(string display) => new(display, NormalizeForMatch(display));
    }

    private sealed record VariantEntry(
        string Identifier,
        string DisplayName,
        IReadOnlyList<string> Family,
        IReadOnlyList<Marker> SelfIdentifying,
        IReadOnlyList<Marker> RefineOnly);

    private static VariantEntry Entry(string id, string display, string[] family, string[] self, string[] refineOnly) =>
        new(id, display, family, [.. self.Select(Marker.Of)], [.. refineOnly.Select(Marker.Of)]);

    /// <summary>
    /// Regional entries before the plain base-language catch-all, and the more specific entry
    /// before a broader one for the same base language.
    /// </summary>
    private static readonly IReadOnlyList<VariantEntry> Table =
    [
        Entry("fre-CA", "French (Canada)", FrenchFamily,
            self: ["VFQ", "VOQ", "Québécois", "Quebecois", "Canadian French", "French Canadian"],
            refineOnly: ["VQ", "Québec", "Quebec", "Canada", "Canadian"]),
        Entry("fre-FR", "French (France)", FrenchFamily,
            self: ["VFF", "Truefrench", "True French"],
            refineOnly: []),
        Entry("fre-BE", "French (Belgium)", FrenchFamily,
            self: ["VFB"],
            refineOnly: []),
        Entry("spa-ES", "Spanish (Spain)", SpanishFamily,
            self: ["Castellano", "Castilian", "European Spanish"],
            refineOnly: []),
        Entry("spa-419", "Spanish (Latin America)", SpanishFamily,
            self: ["Español Latino", "Espanol Latino", "Latin American", "Latin Spanish", "LATAM"],
            refineOnly: ["Latino"]),
        Entry("por-BR", "Portuguese (Brazil)", PortugueseFamily,
            self: ["Brasileiro", "Brazilian", "pt-BR"],
            refineOnly: []),
        Entry("por-PT", "Portuguese (Portugal)", PortugueseFamily,
            self: ["Português Europeu", "Portugues Europeu", "European Portuguese", "pt-PT"],
            refineOnly: ["Europeu"]),
        Entry("zho-Hant", "Chinese (Traditional)", ChineseFamily,
            self: ["CHT", "BIG5", "繁體", "繁体", "Traditional Chinese"],
            refineOnly: ["Traditional"]),
        Entry("zho-Hans", "Chinese (Simplified)", ChineseFamily,
            self: ["CHS", "简体", "簡體", "Simplified Chinese"],
            refineOnly: ["Simplified"]),
        Entry("yue", "Cantonese", ChineseFamily,
            self: ["Cantonese", "廣東話", "广东话", "粵語", "粤语"],
            refineOnly: ["Yue"]),
        Entry("cmn", "Mandarin", ChineseFamily,
            self: ["Mandarin", "普通話", "普通话", "國語", "国语"],
            refineOnly: []),
        Entry("nld-BE", "Flemish", DutchFamily,
            self: ["Vlaams", "Flemish"],
            refineOnly: []),
    ];

    /// <summary>Every identifier this table can produce, for recognizing one in a configured value.</summary>
    private static readonly HashSet<string> KnownIdentifiers = Table.Select(e => e.Identifier).ToHashSet(StringComparer.Ordinal);

    /// <summary>Human-readable name for a plan note, e.g. <c>"French (Canada)"</c>.</summary>
    public static IReadOnlyDictionary<string, string> DisplayNames { get; } =
        Table.ToDictionary(e => e.Identifier, e => e.DisplayName, StringComparer.Ordinal);

    public static string DisplayName(string identifier) => DisplayNames.GetValueOrDefault(identifier, identifier);

    /// <summary>Whether <paramref name="value"/> is one of the identifiers this table can produce.</summary>
    public static bool IsVariantIdentifier(string? value) => value is not null && KnownIdentifiers.Contains(value);
}
