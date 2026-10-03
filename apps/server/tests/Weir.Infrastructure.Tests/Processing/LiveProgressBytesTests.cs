using Weir.Core.Json;
using Weir.Infrastructure.Processing;

namespace Weir.Infrastructure.Tests.Processing;

/// <summary>The byte counts and speed a pass reports, as the live progress keeps them.</summary>
public sealed class LiveProgressBytesTests
{
    [Fact]
    public void A_report_that_gives_bytes_read_and_written_keeps_them()
    {
        var progress = LiveProgress.FromReport(new WireObject().Set("status", "processing").Set("bytes_read", 2_000L).Set("bytes_written", 1_500L));

        Assert.Equal(2_000, progress.BytesRead);
        Assert.Equal(1_500, progress.BytesWritten);
    }

    [Fact]
    public void A_report_without_bytes_keeps_none()
    {
        var progress = LiveProgress.FromReport(new WireObject().Set("status", "processing"));

        Assert.Null(progress.BytesRead);
        Assert.Null(progress.BytesWritten);
    }

    [Fact]
    public void A_negative_byte_count_is_not_kept()
    {
        var progress = LiveProgress.FromReport(new WireObject().Set("status", "processing").Set("bytes_read", -5L));

        Assert.Null(progress.BytesRead);
    }

    [Theory]
    [InlineData("148x", 148.0)]
    [InlineData("1.01x", 1.01)]
    [InlineData(" 22x ", 22.0)]
    public void A_speed_in_multiples_is_its_number(string speed, double multiple)
    {
        Assert.Equal(multiple, PassSpeed.Multiple(speed));
    }

    [Theory]
    [InlineData("12.5 MB/s")]
    [InlineData("N/A")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData(null)]
    public void Any_other_speed_text_has_no_multiple(string? speed)
    {
        Assert.Null(PassSpeed.Multiple(speed));
    }
}
