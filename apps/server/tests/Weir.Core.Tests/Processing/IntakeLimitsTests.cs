using Weir.Core.Processing;

namespace Weir.Core.Tests.Processing;

/// <summary>A library's minimum size and wait: its own when it sets them, Settings › Performance's when it does not.</summary>
public sealed class IntakeLimitsTests
{
    private static readonly ProcessingOperatorSettingsRecord Performance = new() { ProcessingMinInputFileSizeMb = 50, MinFileAgeSeconds = 60 };

    private static ProcessingLibraryRecord Library(long? minFileSizeMb = null, long? minFileAgeSeconds = null) =>
        new() { Name = "Movies", MinFileSizeMb = minFileSizeMb, MinFileAgeSeconds = minFileAgeSeconds };

    [Fact]
    public void A_library_that_sets_neither_follows_Performance()
    {
        Assert.Equal(new IntakeLimits(50, 60), IntakeLimits.Resolve(Library(), Performance));
    }

    [Fact]
    public void A_library_that_sets_both_keeps_its_own_values()
    {
        Assert.Equal(new IntakeLimits(5, 10), IntakeLimits.Resolve(Library(minFileSizeMb: 5, minFileAgeSeconds: 10), Performance));
    }

    [Fact]
    public void A_library_can_set_zero_to_take_any_size_or_wait_no_time()
    {
        Assert.Equal(new IntakeLimits(0, 0), IntakeLimits.Resolve(Library(minFileSizeMb: 0, minFileAgeSeconds: 0), Performance));
    }

    [Fact]
    public void Each_value_is_resolved_on_its_own()
    {
        Assert.Equal(new IntakeLimits(50, 10), IntakeLimits.Resolve(Library(minFileAgeSeconds: 10), Performance));
    }

    [Fact]
    public void A_change_to_Performance_changes_a_library_that_follows_it_and_not_one_that_sets_its_own()
    {
        var changed = Performance with { ProcessingMinInputFileSizeMb = 1, MinFileAgeSeconds = 10 };

        Assert.Equal(new IntakeLimits(1, 10), IntakeLimits.Resolve(Library(), changed));
        Assert.Equal(new IntakeLimits(50, 60), IntakeLimits.Resolve(Library(minFileSizeMb: 50, minFileAgeSeconds: 60), changed));
    }

    [Fact]
    public void A_file_with_no_library_row_follows_Performance()
    {
        Assert.Equal(new IntakeLimits(50, 60), IntakeLimits.Resolve(null, Performance));
    }

    [Fact]
    public void A_negative_value_is_read_as_zero()
    {
        Assert.Equal(new IntakeLimits(0, 0), IntakeLimits.Resolve(Library(minFileSizeMb: -1, minFileAgeSeconds: -1), Performance));
    }
}
