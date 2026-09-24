using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.LibraryMode;

/// <summary>
/// The phrasing half of the re-download-risk manager calls (issue #508 step 2), in the same split as
/// <see cref="ManagerDialectRules"/>: reading Sonarr/Radarr JSON into the neutral shapes
/// <see cref="RedownloadRiskEvaluator"/> works with. The HTTP half lives in
/// <c>Weir.Infrastructure.LibraryMode.ArrRedownloadRiskGateway</c>.
/// </summary>
public static class RedownloadRiskDialect
{
    /// <summary>
    /// A file resource's <c>customFormats</c>/<c>customFormatScore</c>/<c>languages</c>
    /// (Sonarr <c>EpisodeFileResource</c>, openapi.json:8498 at v5-develop; Radarr <c>MovieFileResource</c>,
    /// openapi.json:10819 at develop).
    /// </summary>
    public static FileFormatSnapshot ParseFileFormatSnapshot(WireValue? payload, string managerKind)
    {
        if (payload is not WireObject file)
        {
            return new FileFormatSnapshot([], [], 0);
        }

        var languages = ManagerValues.Dicts(file.Get("languages"))
            .Select(language => ManagerValues.FirstNumber(language, "id"))
            .OfType<System.Numerics.BigInteger>()
            .Select(id => ArrLanguageCatalog.CanonicalCodeFor(managerKind, (int)id))
            .OfType<string>()
            .ToList();

        var customFormats = ManagerValues.Dicts(file.Get("customFormats"))
            .Select(format => ParseCustomFormat(format, managerKind))
            .ToList();

        var score = (int)(ManagerValues.WholeNumber(file.Get("customFormatScore")) ?? 0);
        return new FileFormatSnapshot(languages, customFormats, score);
    }

    private static CustomFormatSnapshot ParseCustomFormat(WireObject format, string managerKind)
    {
        var id = ManagerValues.WholeNumber(format.Get("id")) ?? 0;
        var name = ManagerValues.Text(format.Get("name")) ?? string.Empty;
        var specifications = ManagerValues.Dicts(format.Get("specifications"))
            .Select(specification => ParseSpecification(specification, managerKind))
            .ToList();
        return new CustomFormatSnapshot((long)id, name, specifications);
    }

    private static FormatSpecificationSnapshot ParseSpecification(WireObject specification, string managerKind)
    {
        var implementation = ManagerValues.Text(specification.Get("implementation")) ?? string.Empty;
        var negate = specification.Get("negate")?.IsTruthy ?? false;
        if (implementation != RedownloadRiskEvaluator.LanguageImplementation)
        {
            return new FormatSpecificationSnapshot(implementation, negate, LanguageCode: null, ExceptLanguage: false);
        }

        var fields = ManagerValues.Dicts(specification.Get("fields"));
        var valueField = fields.FirstOrDefault(field => string.Equals(ManagerValues.Text(field.Get("name")), "value", StringComparison.OrdinalIgnoreCase));
        var exceptLanguageField = fields.FirstOrDefault(field =>
            string.Equals(ManagerValues.Text(field.Get("name")), "exceptLanguage", StringComparison.OrdinalIgnoreCase));

        var languageCode = valueField is not null && ManagerValues.WholeNumber(valueField.Get("value")) is { } languageId
            ? ArrLanguageCatalog.CanonicalCodeFor(managerKind, (int)languageId)
            : null;
        var exceptLanguage = exceptLanguageField?.Get("value")?.IsTruthy ?? false;
        return new FormatSpecificationSnapshot(implementation, negate, languageCode, exceptLanguage);
    }

    /// <summary>
    /// A quality profile's <c>upgradeAllowed</c>/<c>cutoffFormatScore</c>/<c>formatItems</c>
    /// (<c>QualityProfileResource</c>, openapi.json:10800 at v5-develop; :11665 at develop). <c>formatItems</c>'
    /// <c>format</c> field is the custom format id the score applies to (<c>ProfileFormatItemResource</c>,
    /// openapi.json:10616 / :11493).
    /// </summary>
    public static QualityProfileSnapshot ParseQualityProfile(WireValue? payload)
    {
        if (payload is not WireObject profile)
        {
            return new QualityProfileSnapshot(UpgradeAllowed: false, CutoffFormatScore: 0, FormatScores: new Dictionary<long, int>());
        }

        var upgradeAllowed = profile.Get("upgradeAllowed")?.IsTruthy ?? false;
        var cutoff = (int)(ManagerValues.WholeNumber(profile.Get("cutoffFormatScore")) ?? 0);
        var scores = new Dictionary<long, int>();
        foreach (var item in ManagerValues.Dicts(profile.Get("formatItems")))
        {
            if (ManagerValues.WholeNumber(item.Get("format")) is not { } formatId)
            {
                continue;
            }

            var score = (int)(ManagerValues.WholeNumber(item.Get("score")) ?? 0);
            scores[(long)formatId] = score;
        }

        return new QualityProfileSnapshot(upgradeAllowed, cutoff, scores);
    }
}
