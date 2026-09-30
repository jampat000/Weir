using Weir.Core.LibraryMode;
using Weir.Core.Processing;
using Weir.Core.Rules;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Processing.RemuxPass;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// The rules that scan, plan and clean a library's existing files. A library chooses its own profile on the Library page;
/// until it does, it follows its workflow's profile, and with neither the shipped defaults apply.
/// </summary>
public static class LibraryModeRules
{
    /// <summary>The profile id in effect: the library's own choice, else its workflow's.</summary>
    public static long? EffectiveRuleSetId(ProcessingLibraryRecord library, LibrarySettings settings)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(settings);
        return settings.RuleSetId ?? library.RuleSetId;
    }

    public static async Task<ProcessingRulesConfig> ForAsync(
        UnitOfWork uow, LibraryStore libraries, ProcessingLibraryRecord library, LibrarySettings settings)
    {
        ArgumentNullException.ThrowIfNull(libraries);
        var ruleSet = EffectiveRuleSetId(library, settings) is { } ruleSetId
            ? await libraries.GetRuleSetAsync(uow, ruleSetId).ConfigureAwait(false)
            : null;
        return ruleSet is not null ? RemuxPassPaths.RulesConfigFor(ruleSet) : RuleSetConversion.ToRulesConfig(null);
    }
}
