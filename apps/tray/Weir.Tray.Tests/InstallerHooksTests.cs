using System.Reflection;
using Velopack;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// Velopack's own start-up install of a waiting update is off, so <see cref="UpdateOnStart"/> is the only start-up
/// path that installs one and its guards always apply (#865).
/// </summary>
public sealed class InstallerHooksTests
{
    [Fact]
    public void Velopack_does_not_install_a_waiting_update_by_itself_when_the_tray_starts()
    {
        var hooks = Program.BuildInstallerHooks([]);

        Assert.False(AutoApplyOnStartup(hooks));
    }

    // Velopack exposes the setter but not the value, and the builder's Run() acts only inside an installed app.
    private static bool AutoApplyOnStartup(VelopackApp hooks)
    {
        var field = typeof(VelopackApp)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(bool) && f.Name.Contains("AutoApply", StringComparison.OrdinalIgnoreCase));
        return (bool)field.GetValue(hooks)!;
    }
}
