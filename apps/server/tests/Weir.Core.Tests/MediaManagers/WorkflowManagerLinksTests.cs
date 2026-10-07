using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

public sealed class WorkflowManagerLinksTests
{
    [Fact]
    public void A_weir_only_workflow_keeps_nothing_and_is_scanned()
    {
        var links = WorkflowManagerLinks.None;

        Assert.False(links.IsLinked);
        Assert.False(links.KeepsOriginals);
        Assert.False(links.HandedOffByManager);
    }

    [Theory]
    [InlineData("deluno")]
    [InlineData("sonarr")]
    [InlineData("radarr")]
    [InlineData("native")]
    [InlineData(" Deluno ")]
    public void A_workflow_linked_to_any_kind_of_manager_keeps_its_originals(string kind)
    {
        Assert.True(new WorkflowManagerLinks([kind]).KeepsOriginals);
    }

    [Theory]
    [InlineData("deluno", true)]
    [InlineData(" Deluno ", true)]
    [InlineData("sonarr", false)]
    [InlineData("radarr", false)]
    [InlineData("native", false)]
    public void Only_a_link_to_deluno_means_the_manager_hands_over_the_downloads(string kind, bool handedOff)
    {
        Assert.Equal(handedOff, new WorkflowManagerLinks([kind]).HandedOffByManager);
    }

    [Theory]
    [InlineData("deluno", "Deluno")]
    [InlineData("sonarr", "Sonarr")]
    [InlineData("radarr", "Radarr")]
    [InlineData("native", "your media manager")]
    public void The_reason_a_download_stays_names_the_manager(string kind, string name)
    {
        Assert.Equal(
            $"This workflow is linked to {name}, so the original stays with your download client, which may still be seeding.",
            new WorkflowManagerLinks([kind]).KeptOriginalReason);
    }

    [Fact]
    public void Several_managers_are_each_named_once()
    {
        var links = new WorkflowManagerLinks(["deluno", "sonarr", "deluno"]);

        Assert.Contains("linked to Deluno and Sonarr,", links.KeptOriginalReason, StringComparison.Ordinal);
        Assert.Equal("Deluno hands this workflow its downloads, so Weir does not scan its watched folder.", links.ScanSkippedReason);
    }
}
