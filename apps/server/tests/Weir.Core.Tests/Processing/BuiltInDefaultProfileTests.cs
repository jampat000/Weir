using System.Text.Json;
using Weir.Core.Processing;
using Weir.Core.Rules;

namespace Weir.Core.Tests.Processing;

/// <summary>
/// Every library has a profile (3.2). A library that had none ran on Weir's built-in rules, so the "Movies default" and
/// "TV default" profiles it is given must be those rules exactly, or giving it a profile would change its files.
/// </summary>
public sealed class BuiltInDefaultProfileTests
{
    [Fact]
    public void The_default_profile_is_exactly_the_rules_a_library_without_one_ran_on()
    {
        var profile = RuleSetConversion.BuiltInDefaults("Movies default");
        Assert.Contains("\"SubtitleMode\":\"remove_all\"", JsonSerializer.Serialize(RemuxRules.DefaultConfig()), StringComparison.Ordinal);

        Assert.Equal("Movies default", profile.Name);
        Assert.Equal(
            JsonSerializer.Serialize(RemuxRules.DefaultConfig()),
            JsonSerializer.Serialize(RuleSetConversion.ToRulesConfig(profile)));
    }
}
