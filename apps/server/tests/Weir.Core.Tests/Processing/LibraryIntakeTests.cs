using Weir.Core.Processing;

namespace Weir.Core.Tests.Processing;

/// <summary>The one wait a workflow holds, and how the three waits it replaced add up to it.</summary>
public sealed class LibraryIntakeTests
{
    [Fact]
    public void A_new_workflow_waits_sixty_seconds_and_takes_files_of_fifty_megabytes_and_up()
    {
        var library = new ProcessingLibraryRecord { Name = "Movies" };

        Assert.Equal(60, library.ReadyAfterSeconds);
        Assert.Equal(50, library.MinFileSizeMb);
    }

    [Theory]
    [InlineData(60, 0, 30, 60)]
    [InlineData(60, 5, 30, 360)]
    [InlineData(10, 0, 90, 90)]
    [InlineData(60, 0, 0, 60)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 2, 0, 120)]
    [InlineData(0, 0, 45, 45)]
    public void The_wait_is_the_longer_of_the_age_plus_hold_and_the_size_wait(long age, long holdMinutes, long sizeStable, long expected)
    {
        Assert.Equal(expected, LibraryIntake.ReadyAfterFromThreeWaits(age, holdMinutes, sizeStable));
    }

    [Fact]
    public void A_negative_wait_counts_as_none()
    {
        Assert.Equal(0, LibraryIntake.ReadyAfterFromThreeWaits(-5, -1, -30));
    }

    [Fact]
    public void The_wait_never_exceeds_what_a_workflow_can_hold()
    {
        Assert.Equal(LibraryIntake.MaxReadyAfterSeconds, LibraryIntake.ReadyAfterFromThreeWaits(long.MaxValue / 2, 0, 0));
    }
}
