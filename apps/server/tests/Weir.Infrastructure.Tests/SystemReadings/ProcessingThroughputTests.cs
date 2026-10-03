using Microsoft.Extensions.Time.Testing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

/// <summary>Weir's own work as the System view sees it, from what the running passes report.</summary>
public sealed class ProcessingThroughputTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly LiveProgressStore _progress = new();

    private static LiveProgress Pass(long? read, long? written, string? speed = null) =>
        new(50, null, null, "processing", speed, null, [], [], BytesRead: read, BytesWritten: written);

    [Fact]
    public void With_nothing_running_nothing_is_read_or_written()
    {
        var throughput = new ProcessingThroughput(_progress, _time);
        throughput.Read();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(new ProcessingReading(0, 0, 0, 0), throughput.Read());
    }

    [Fact]
    public void A_pass_that_is_further_on_than_it_was_adds_the_bytes_it_moved_over_the_seconds_between()
    {
        var throughput = new ProcessingThroughput(_progress, _time);
        _progress.Update("a.mkv", Pass(read: 1_000, written: 500));
        throughput.Read();

        _time.Advance(TimeSpan.FromSeconds(2));
        _progress.Update("a.mkv", Pass(read: 11_000, written: 5_500));
        var reading = throughput.Read();

        Assert.Equal(5_000, reading.ReadBytesPerSecond);
        Assert.Equal(2_500, reading.WriteBytesPerSecond);
        Assert.Equal(1, reading.Running);
    }

    [Fact]
    public void Several_passes_add_together()
    {
        var throughput = new ProcessingThroughput(_progress, _time);
        _progress.Update("a.mkv", Pass(0, 0));
        _progress.Update("b.mkv", Pass(0, 0));
        throughput.Read();

        _time.Advance(TimeSpan.FromSeconds(1));
        _progress.Update("a.mkv", Pass(100, 100));
        _progress.Update("b.mkv", Pass(300, 300));

        Assert.Equal(400, throughput.Read().ReadBytesPerSecond);
    }

    [Fact]
    public void A_pass_that_began_since_the_last_reading_counts_from_its_start()
    {
        var throughput = new ProcessingThroughput(_progress, _time);
        throughput.Read();

        _time.Advance(TimeSpan.FromSeconds(1));
        _progress.Update("new.mkv", Pass(read: 700, written: 700));

        Assert.Equal(700, throughput.Read().WriteBytesPerSecond);
    }

    [Fact]
    public void A_pass_that_ended_adds_nothing_and_does_not_make_the_rate_negative()
    {
        var throughput = new ProcessingThroughput(_progress, _time);
        _progress.Update("a.mkv", Pass(5_000, 5_000));
        throughput.Read();

        _time.Advance(TimeSpan.FromSeconds(1));
        _progress.Remove("a.mkv");

        var reading = throughput.Read();

        Assert.Equal(0, reading.ReadBytesPerSecond);
        Assert.Equal(0, reading.Running);
    }

    [Fact]
    public void A_pass_that_reports_no_bytes_adds_none()
    {
        var throughput = new ProcessingThroughput(_progress, _time);
        _progress.Update("a.mkv", Pass(null, null));
        throughput.Read();

        _time.Advance(TimeSpan.FromSeconds(1));
        _progress.Update("a.mkv", Pass(null, null));

        Assert.Equal(new ProcessingReading(1, 0, 0, 0), throughput.Read());
    }

    [Fact]
    public void Speed_adds_up_the_multiples_and_leaves_out_a_copys_megabytes_a_second()
    {
        var throughput = new ProcessingThroughput(_progress, _time);
        _progress.Update("a.mkv", Pass(0, 0, speed: "148x"));
        _progress.Update("b.mkv", Pass(0, 0, speed: "74.5x"));
        _progress.Update("c.mkv", Pass(0, 0, speed: "12.5 MB/s"));
        _progress.Update("d.mkv", Pass(0, 0, speed: "N/A"));

        Assert.Equal(222.5, throughput.Read().Speed);
    }
}
