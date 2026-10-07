using Weir.Tray.Firewall;
using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests.Firewall;

/// <summary>
/// An older Weir rule (Domain and Private) is widened by asking Windows once at start-up: only with network access
/// on, an interactive start and a rule that exists and is narrower than the current one. The question is recorded
/// before it is shown and never repeats, whatever the answer.
/// </summary>
public sealed class FirewallRuleWideningTests : IDisposable
{
    private const string InstallRoot = @"C:\Users\test\AppData\Local\Weir\current";
    private const FirewallProfiles DomainAndPrivate = FirewallProfiles.Domain | FirewallProfiles.Private;

    private readonly TempDirectory _temp = TempDirectory.AsWeirHome();
    private readonly FakeAccess _firewall = new();
    private readonly List<string> _log = [];

    public void Dispose() => _temp.Dispose();

    private string Home => _temp.Path;

    private string MarkerPath => Path.Combine(Home, FirewallRuleWidening.MarkerFileName);

    private static FirewallRule RuleFor(FirewallProfiles profiles, bool enabled = true, string name = WeirFirewallRule.RuleName) =>
        new(name, WeirFirewallRule.ServerProgramPath(InstallRoot), FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, profiles, enabled);

    private FirewallElevation.Outcome? Start(
        IFirewallPolicy policy,
        ListenScope scope = ListenScope.OtherDevices,
        bool interactive = true) =>
        FirewallRuleWidening.AskIfNeeded(Home, scope, interactive, () => policy, InstallRoot, _firewall, _log.Add);

    private sealed class FakeAccess : IFirewallAccess
    {
        public FirewallElevation.Outcome Answer { get; set; } = FirewallElevation.Outcome.Configured;

        public int Asked { get; private set; }

        public bool MarkerExistedWhenAsked { get; private set; }

        public string MarkerPath { get; set; } = "";

        public bool AllowsWeirIn() => true;

        public FirewallElevation.Outcome AskToAllow()
        {
            Asked++;
            MarkerExistedWhenAsked = File.Exists(MarkerPath);
            return Answer;
        }
    }

    // -- When it asks ----------------------------------------------------------

    [Theory]
    [InlineData((int)DomainAndPrivate)]
    [InlineData((int)FirewallProfiles.Private)]
    [InlineData((int)FirewallProfiles.Public)]
    [InlineData((int)(FirewallProfiles.Domain | FirewallProfiles.Public))]
    public void A_narrow_rule_with_network_access_on_at_an_interactive_start_is_asked_to_widen(int profiles)
    {
        var outcome = Start(new FakeFirewallPolicy(RuleFor((FirewallProfiles)profiles)));

        Assert.Equal(FirewallElevation.Outcome.Configured, outcome);
        Assert.Equal(1, _firewall.Asked);
    }

    [Fact]
    public void A_rule_Windows_made_itself_counts_as_the_rule_to_widen()
    {
        var outcome = Start(new FakeFirewallPolicy(RuleFor(DomainAndPrivate, name: "WeirServer.exe")));

        Assert.NotNull(outcome);
        Assert.Equal(1, _firewall.Asked);
    }

    [Fact]
    public void Rules_that_together_cover_every_profile_are_left_alone()
    {
        var policy = new FakeFirewallPolicy(
            RuleFor(DomainAndPrivate),
            RuleFor(FirewallProfiles.Public, name: "WeirServer.exe"));

        Assert.Null(Start(policy));
        Assert.Equal(0, _firewall.Asked);
    }

    [Fact]
    public void A_disabled_rule_does_not_count_towards_the_profiles_covered()
    {
        var policy = new FakeFirewallPolicy(
            RuleFor(DomainAndPrivate),
            RuleFor(FirewallProfiles.Public, enabled: false, name: "WeirServer.exe"));

        Assert.NotNull(Start(policy));
    }

    [Fact]
    public void A_rule_for_another_program_is_not_Weirs_rule()
    {
        var other = new FirewallRule("Other", @"C:\Other\other.exe", FirewallRuleAction.Allow, FirewallRuleDirection.Inbound, DomainAndPrivate, Enabled: true);

        Assert.Null(Start(new FakeFirewallPolicy(other)));
        Assert.Equal(0, _firewall.Asked);
    }

    [Fact]
    public void A_rule_that_already_covers_every_profile_asks_nothing_and_writes_no_marker()
    {
        Assert.Null(Start(new FakeFirewallPolicy(WeirFirewallRule.DesiredRule(InstallRoot))));

        Assert.Equal(0, _firewall.Asked);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void No_rule_at_all_asks_nothing_because_the_first_run_and_the_network_choice_own_that()
    {
        Assert.Null(Start(new FakeFirewallPolicy()));

        Assert.Equal(0, _firewall.Asked);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void Network_access_off_asks_nothing()
    {
        Assert.Null(Start(new FakeFirewallPolicy(RuleFor(DomainAndPrivate)), scope: ListenScope.ThisPcOnly));

        Assert.Equal(0, _firewall.Asked);
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact]
    public void A_start_without_a_person_to_answer_asks_nothing_and_leaves_the_question_for_a_later_start()
    {
        var policy = new FakeFirewallPolicy(RuleFor(DomainAndPrivate));

        Assert.Null(Start(policy, interactive: false));
        Assert.Equal(0, _firewall.Asked);
        Assert.False(File.Exists(MarkerPath));

        Assert.NotNull(Start(policy));
    }

    [Fact]
    public void A_firewall_that_cannot_be_read_asks_nothing_and_is_looked_at_again_next_start()
    {
        FirewallRuleWidening.AskIfNeeded(Home, ListenScope.OtherDevices, true, () => throw new InvalidOperationException("The firewall service is not running."), InstallRoot, _firewall, _log.Add);

        Assert.Equal(0, _firewall.Asked);
        Assert.False(File.Exists(MarkerPath));
        Assert.Contains(_log, line => line.Contains("could not be read", StringComparison.Ordinal));
    }

    // -- Once only ---------------------------------------------------------------

    [Theory]
    [InlineData((int)FirewallElevation.Outcome.Configured)]
    [InlineData((int)FirewallElevation.Outcome.Declined)]
    [InlineData((int)FirewallElevation.Outcome.Failed)]
    public void The_question_is_recorded_whatever_the_answer_and_never_asked_again(int answer)
    {
        _firewall.Answer = (FirewallElevation.Outcome)answer;
        var policy = new FakeFirewallPolicy(RuleFor(DomainAndPrivate));

        Assert.Equal((FirewallElevation.Outcome)answer, Start(policy));
        Assert.True(File.Exists(MarkerPath));

        Assert.Null(Start(policy));
        Assert.Equal(1, _firewall.Asked);
    }

    [Fact]
    public void The_question_is_recorded_before_the_prompt_is_shown()
    {
        _firewall.MarkerPath = MarkerPath;

        Start(new FakeFirewallPolicy(RuleFor(DomainAndPrivate)));

        Assert.True(_firewall.MarkerExistedWhenAsked);
    }

    [Fact]
    public void A_question_that_cannot_be_recorded_is_not_asked()
    {
        Directory.CreateDirectory(MarkerPath);

        Start(new FakeFirewallPolicy(RuleFor(DomainAndPrivate)));

        Assert.Equal(0, _firewall.Asked);
        Assert.Contains(_log, line => line.Contains("could not be recorded", StringComparison.Ordinal));
    }

    // -- What the log says ---------------------------------------------------------

    [Fact]
    public void The_log_says_which_profiles_the_rule_covers_and_what_the_answer_was()
    {
        Start(new FakeFirewallPolicy(RuleFor(DomainAndPrivate)));

        Assert.Equal(
            [
                "Firewall: Weir's rule covers Domain, Private only; asking Windows once to widen it to every network type.",
                "Firewall: Configured. Weir's rule now covers every network type.",
            ],
            _log);
    }

    [Theory]
    [InlineData((int)FirewallElevation.Outcome.Declined, "Declined")]
    [InlineData((int)FirewallElevation.Outcome.Failed, "Failed")]
    public void The_log_names_a_decline_and_a_failure(int answer, string word)
    {
        _firewall.Answer = (FirewallElevation.Outcome)answer;

        Start(new FakeFirewallPolicy(RuleFor(DomainAndPrivate)));

        Assert.StartsWith($"Firewall: {word}.", _log[^1], StringComparison.Ordinal);
    }
}
