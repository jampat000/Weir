using Weir.Core.Processing;

namespace Weir.Core.Tests.Processing;

public sealed class FfmpegStrictnessLevelsTests
{
    [Theory]
    [InlineData("very")]
    [InlineData("strict")]
    [InlineData("unofficial")]
    [InlineData("experimental")]
    public void A_level_other_than_ffmpegs_own_default_is_passed_before_the_input(string level)
    {
        Assert.Equal(["-strict", level], FfmpegStrictnessLevels.InputFlags(level));
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("something else")]
    [InlineData("")]
    [InlineData(null)]
    public void Ffmpegs_default_and_unrecognised_values_add_no_options(string? level)
    {
        Assert.Empty(FfmpegStrictnessLevels.InputFlags(level));
    }
}
