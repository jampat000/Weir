using Weir.Core.Json;
using Weir.Infrastructure.Settings;

namespace Weir.Infrastructure.Tests.Settings;

/// <summary>
/// A backup made before a workflow's wait and minimum size lived on the workflow alone still restores, and each workflow comes
/// out with the longest wait it had and the minimum size it used, the same as the upgrade does for a database.
/// </summary>
public sealed class ConfigurationBundleIntakeUpgradeTests
{
    private static WireObject Performance(long ageSeconds = 60, long minSizeMb = 50) =>
        new WireObject().Set("min_file_age_seconds", ageSeconds).Set("min_input_file_size_mb", minSizeMb);

    private static WireObject OldWorkflow(
        long? ageSeconds = 60, long holdMinutes = 0, long sizeStableSeconds = 30, bool ignoreSizeChanges = false, long? minSizeMb = 50) =>
        new WireObject()
            .Set("name", "Movies")
            .Set("min_file_age_seconds", ageSeconds)
            .Set("hold_minutes", holdMinutes)
            .Set("file_detection_interval_seconds", sizeStableSeconds)
            .Set("ignore_size_changes", ignoreSizeChanges)
            .Set("min_file_size_mb", minSizeMb);

    private static void Upgrade(WireObject performance, params WireObject[] workflows) =>
        ConfigurationBundleIntakeUpgrade.Apply(performance, workflows);

    private static long Number(WireObject row, string key) => (long)((WireInteger)row[key]).Value;

    [Fact]
    public void A_workflow_gets_its_age_plus_its_hold()
    {
        var workflow = OldWorkflow(ageSeconds: 60, holdMinutes: 5);

        Upgrade(Performance(), workflow);

        Assert.Equal(360, Number(workflow, "ready_after_seconds"));
    }

    [Fact]
    public void A_size_wait_longer_than_the_age_plus_hold_is_the_wait()
    {
        var workflow = OldWorkflow(ageSeconds: 10, sizeStableSeconds: 90);

        Upgrade(Performance(), workflow);

        Assert.Equal(90, Number(workflow, "ready_after_seconds"));
    }

    [Fact]
    public void A_workflow_with_no_age_of_its_own_gets_the_Performance_age()
    {
        var workflow = OldWorkflow(ageSeconds: null, holdMinutes: 1);

        Upgrade(Performance(ageSeconds: 200), workflow);

        Assert.Equal(260, Number(workflow, "ready_after_seconds"));
    }

    [Fact]
    public void A_workflow_that_ignored_size_changes_gains_no_size_wait()
    {
        var workflow = OldWorkflow(ageSeconds: 20, sizeStableSeconds: 300, ignoreSizeChanges: true);

        Upgrade(Performance(), workflow);

        Assert.Equal(20, Number(workflow, "ready_after_seconds"));
    }

    [Fact]
    public void A_workflow_that_waited_for_nothing_still_waits_for_nothing()
    {
        var workflow = OldWorkflow(ageSeconds: 0, holdMinutes: 0, sizeStableSeconds: 0);

        Upgrade(Performance(), workflow);

        Assert.Equal(0, Number(workflow, "ready_after_seconds"));
    }

    [Fact]
    public void A_workflow_with_no_minimum_size_of_its_own_gets_the_Performance_minimum()
    {
        var workflow = OldWorkflow(minSizeMb: null);

        Upgrade(Performance(minSizeMb: 75), workflow);

        Assert.Equal(75, Number(workflow, "min_file_size_mb"));
    }

    [Fact]
    public void A_minimum_size_a_workflow_set_is_kept_including_zero()
    {
        var own = OldWorkflow(minSizeMb: 200);
        var none = OldWorkflow(minSizeMb: 0);

        Upgrade(Performance(minSizeMb: 75), own, none);

        Assert.Equal((200, 0), (Number(own, "min_file_size_mb"), Number(none, "min_file_size_mb")));
    }

    [Fact]
    public void A_backup_with_no_Performance_wait_or_size_uses_the_old_defaults()
    {
        var workflow = OldWorkflow(ageSeconds: null, sizeStableSeconds: 30, minSizeMb: null);

        Upgrade(new WireObject(), workflow);

        Assert.Equal((60, 50), (Number(workflow, "ready_after_seconds"), Number(workflow, "min_file_size_mb")));
    }

    [Fact]
    public void A_workflow_already_in_the_current_shape_is_left_as_it_is()
    {
        var workflow = new WireObject().Set("name", "Movies").Set("ready_after_seconds", 5L).Set("min_file_size_mb", 0L);

        Upgrade(Performance(ageSeconds: 500, minSizeMb: 500), workflow);

        Assert.Equal((5, 0), (Number(workflow, "ready_after_seconds"), Number(workflow, "min_file_size_mb")));
    }
}
