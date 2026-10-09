using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Infrastructure.Auth;
using Weir.Infrastructure.Tests.Media;

namespace Weir.Infrastructure.Tests.Auth;

/// <summary>The setup code a new install publishes at start-up: an instruction to follow, not a problem to look into.</summary>
public sealed class SetupCodeGateTests : IDisposable
{
    private readonly TempDirectory _home = new();

    public void Dispose() => _home.Dispose();

    private SetupCodeGate Gate() => new(WeirOptionsLoader.Load(new RuntimeEnvironment(
        new Dictionary<string, string>(StringComparer.Ordinal) { ["WEIR_HOME"] = _home.Path },
        OperatingSystem.IsWindows(),
        _home.Path,
        _home.Path)));

    [Fact]
    public void An_install_with_no_account_says_where_to_find_the_setup_code_as_information_not_a_warning()
    {
        var logger = new ListLogger<SetupCodeGate>();
        var codeFile = _home.Join("setup-code");

        Gate().EnsureStateForStartup(adminExists: false, logger);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        var code = File.ReadAllText(codeFile).Trim();
        Assert.Equal(
            $"Weir has no account yet. To create one from another device, enter this setup code: {code}. It is also in {codeFile}.",
            entry.Message);
    }

    [Fact]
    public void An_install_with_an_account_says_nothing_and_leaves_no_code()
    {
        var logger = new ListLogger<SetupCodeGate>();
        var gate = Gate();
        gate.EnsureStateForStartup(adminExists: false, new ListLogger<SetupCodeGate>());

        gate.EnsureStateForStartup(adminExists: true, logger);

        Assert.Empty(logger.Entries);
        Assert.False(gate.HasCode);
        Assert.False(File.Exists(_home.Join("setup-code")));
    }
}
