using Weir.Core.Media;

namespace Weir.Core.Tests.Media;

/// <summary>
/// Whether an ffmpeg progress stream is getting anywhere. ffmpeg 9 keeps printing a block every half second while it is stuck on a
/// read that never returns, with the same numbers each time, so a block arriving proves nothing.
/// </summary>
public sealed class FfmpegProgressAdvanceTests
{
    private static string[] Block(string frame, string size, string outTime, string progress = "continue") =>
        [$"frame={frame}", "fps=0.00", $"total_size={size}", $"out_time_us={outTime}", $"progress={progress}"];

    private static List<bool> Feed(FfmpegProgressAdvance advance, params string[][] blocks) =>
        [.. blocks.SelectMany(block => block).Select(advance.Feed)];

    private static bool[] Closings(List<bool> results, int blockSize = 5) =>
        [.. results.Where((_, index) => index % blockSize == blockSize - 1)];

    [Fact]
    public void A_block_with_the_same_numbers_as_the_one_before_is_not_progress()
    {
        var stuck = Block("26", "N/A", "2500000");

        var results = Feed(new FfmpegProgressAdvance(), stuck, stuck, stuck, stuck);

        Assert.Equal([true, false, false, false], Closings(results));
        Assert.All(results.Where((_, index) => index % 5 != 4), advanced => Assert.False(advanced));
    }

    [Fact]
    public void A_block_in_which_the_output_time_has_grown_is_progress()
    {
        var results = Feed(new FfmpegProgressAdvance(), Block("26", "N/A", "2500000"), Block("26", "N/A", "3000000"), Block("26", "N/A", "3000000"));

        Assert.Equal([true, true, false], Closings(results));
    }

    [Fact]
    public void A_block_in_which_the_output_size_has_grown_is_progress_even_when_the_time_has_not()
    {
        var results = Feed(new FfmpegProgressAdvance(), Block("0", "1000", "N/A"), Block("0", "2000", "N/A"), Block("0", "2000", "N/A"));

        Assert.Equal([true, true, false], Closings(results));
    }

    [Fact]
    public void A_block_in_which_only_the_frame_count_has_grown_is_progress()
    {
        var results = Feed(new FfmpegProgressAdvance(), Block("10", "N/A", "N/A"), Block("11", "N/A", "N/A"), Block("11", "N/A", "N/A"));

        Assert.Equal([true, true, false], Closings(results));
    }

    [Fact]
    public void Numbers_that_fall_back_are_not_progress_and_do_not_lower_what_counts_as_further()
    {
        var results = Feed(new FfmpegProgressAdvance(), Block("5", "N/A", "3000000"), Block("5", "N/A", "1000000"), Block("5", "N/A", "2000000"), Block("6", "N/A", "3000000"));

        Assert.Equal([true, false, false, true], Closings(results));
    }

    [Fact]
    public void Not_available_is_not_a_number()
    {
        var results = Feed(new FfmpegProgressAdvance(), Block("N/A", "N/A", "N/A"), Block("N/A", "N/A", "N/A"));

        Assert.Equal([false, false], Closings(results));
    }

    [Fact]
    public void The_final_block_is_progress_whatever_its_numbers_and_is_recognised()
    {
        var advance = new FfmpegProgressAdvance();
        var stuck = Block("26", "N/A", "2500000");

        var results = Feed(advance, stuck, Block("26", "N/A", "2500000", "end"));

        Assert.Equal([true, true], Closings(results));
        Assert.True(FfmpegProgressAdvance.IsEnd("progress=end"));
        Assert.True(FfmpegProgressAdvance.IsEnd("progress=end\r"));
        Assert.False(FfmpegProgressAdvance.IsEnd("progress=continue"));
    }
}
