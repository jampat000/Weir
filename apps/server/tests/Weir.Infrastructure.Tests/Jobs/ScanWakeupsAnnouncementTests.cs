using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Jobs;
using Weir.Infrastructure.Tests.Activity;

namespace Weir.Infrastructure.Tests.Jobs;

/// <summary>The next look at a workflow is not stored, so the screens counting down to it are told when it moves.</summary>
public sealed class ScanWakeupsAnnouncementTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly DataChangePublisher _changes = new();
    private readonly PublishedTopics _published;
    private readonly ScanWakeups _looks;

    public ScanWakeupsAnnouncementTests()
    {
        _looks = new ScanWakeups(_changes);
        _published = new PublishedTopics(_changes);
    }

    public void Dispose() => _published.Dispose();

    [Fact]
    public async Task A_new_periodic_look_moves_the_workflows()
    {
        _looks.RecordNextPeriodic(3, T0.AddMinutes(5));

        Assert.Equal([DataTopics.Libraries], await _published.TakeAsync());
    }

    [Fact]
    public async Task The_same_look_recorded_again_moves_nothing()
    {
        _looks.RecordNextPeriodic(3, T0.AddMinutes(5));
        await _published.TakeAsync();

        _looks.RecordNextPeriodic(3, T0.AddMinutes(5));

        Assert.Empty(await _published.TakeAsync());
    }

    [Fact]
    public async Task A_booking_that_comes_before_the_periodic_look_moves_it_and_a_later_one_does_not()
    {
        _looks.RecordNextPeriodic(3, T0.AddMinutes(5));
        await _published.TakeAsync();

        _looks.Request(3, T0.AddSeconds(40));
        Assert.Equal([DataTopics.Libraries], await _published.TakeAsync());

        _looks.Request(3, T0.AddMinutes(2));
        Assert.Empty(await _published.TakeAsync());
    }

    [Fact]
    public async Task Using_up_a_due_booking_moves_the_look_back_to_the_periodic_one()
    {
        _looks.RecordNextPeriodic(3, T0.AddMinutes(5));
        _looks.Request(3, T0.AddSeconds(40));
        await _published.TakeAsync();

        Assert.False(_looks.TakeDue(3, T0.AddSeconds(39)));
        Assert.Empty(await _published.TakeAsync());

        Assert.True(_looks.TakeDue(3, T0.AddSeconds(40)));
        Assert.Equal([DataTopics.Libraries], await _published.TakeAsync());
        Assert.Equal(T0.AddMinutes(5), _looks.NextLookFor(3));
    }

    [Fact]
    public async Task A_workflow_that_is_no_longer_looked_at_moves_the_workflows_once()
    {
        _looks.RecordNextPeriodic(3, T0.AddMinutes(5));
        await _published.TakeAsync();

        _looks.ForgetPeriodic(3);
        _looks.ForgetPeriodic(3);

        Assert.Equal([DataTopics.Libraries], await _published.TakeAsync());
    }

    [Fact]
    public void Looks_work_without_a_stream_to_tell()
    {
        var alone = new ScanWakeups();

        alone.RecordNextPeriodic(3, T0);

        Assert.Equal(T0, alone.NextLookFor(3));
    }
}
