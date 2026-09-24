using Weir.Core.Auth;

namespace Weir.Core.Tests.Auth;

/// <summary>Exponential per-account login backoff, driven with a fake clock so the growth is exact and not wall-clock-flaky.</summary>
public sealed class UsernameLoginBackoffTests
{
    [Fact]
    public void The_first_five_failures_in_the_window_are_free()
    {
        var time = new ManualTimeProvider();
        var backoff = new UsernameLoginBackoff(time);

        for (var i = 0; i < UsernameLoginBackoff.FreeAttempts; i++)
        {
            Assert.Equal(TimeSpan.Zero, backoff.TimeUntilAllowed("alice"));
            backoff.RecordFailure("alice");
        }

        Assert.Equal(TimeSpan.Zero, backoff.TimeUntilAllowed("alice"));
    }

    [Fact]
    public void The_wait_doubles_with_each_failure_past_the_free_attempts()
    {
        var time = new ManualTimeProvider();
        var backoff = new UsernameLoginBackoff(time);
        for (var i = 0; i < UsernameLoginBackoff.FreeAttempts; i++)
        {
            backoff.RecordFailure("alice");
        }

        backoff.RecordFailure("alice"); // 1st failure past the free attempts: 2^1 = 2s
        Assert.Equal(TimeSpan.FromSeconds(2), backoff.TimeUntilAllowed("alice"));

        time.Advance(TimeSpan.FromSeconds(2));
        backoff.RecordFailure("alice"); // 2nd: 2^2 = 4s
        Assert.Equal(TimeSpan.FromSeconds(4), backoff.TimeUntilAllowed("alice"));

        time.Advance(TimeSpan.FromSeconds(4));
        backoff.RecordFailure("alice"); // 3rd: 2^3 = 8s
        Assert.Equal(TimeSpan.FromSeconds(8), backoff.TimeUntilAllowed("alice"));
    }

    [Fact]
    public void The_wait_never_exceeds_the_maximum_backoff()
    {
        var time = new ManualTimeProvider();
        var backoff = new UsernameLoginBackoff(time);

        // Enough failures, back to back, that 2^(failures - FreeAttempts) is far past the cap.
        for (var i = 0; i < UsernameLoginBackoff.FreeAttempts + 30; i++)
        {
            backoff.RecordFailure("alice");
        }

        Assert.Equal(TimeSpan.FromSeconds(UsernameLoginBackoff.MaxBackoffSeconds), backoff.TimeUntilAllowed("alice"));
    }

    [Fact]
    public void The_wait_expires_once_the_window_has_passed_since_the_last_failure()
    {
        var time = new ManualTimeProvider();
        var backoff = new UsernameLoginBackoff(time);
        for (var i = 0; i <= UsernameLoginBackoff.FreeAttempts; i++)
        {
            backoff.RecordFailure("alice");
        }

        Assert.True(backoff.TimeUntilAllowed("alice") > TimeSpan.Zero);

        time.Advance(TimeSpan.FromSeconds(UsernameLoginBackoff.WindowSeconds + 1));

        Assert.Equal(TimeSpan.Zero, backoff.TimeUntilAllowed("alice"));
    }

    [Fact]
    public void A_success_clears_the_accounts_history()
    {
        var time = new ManualTimeProvider();
        var backoff = new UsernameLoginBackoff(time);
        for (var i = 0; i <= UsernameLoginBackoff.FreeAttempts; i++)
        {
            backoff.RecordFailure("alice");
        }

        Assert.True(backoff.TimeUntilAllowed("alice") > TimeSpan.Zero);

        backoff.RecordSuccess("alice");

        Assert.Equal(TimeSpan.Zero, backoff.TimeUntilAllowed("alice"));
    }

    [Fact]
    public void Usernames_are_tracked_independently_and_case_insensitively()
    {
        var time = new ManualTimeProvider();
        var backoff = new UsernameLoginBackoff(time);
        for (var i = 0; i <= UsernameLoginBackoff.FreeAttempts; i++)
        {
            backoff.RecordFailure("Alice");
        }

        Assert.True(backoff.TimeUntilAllowed("alice") > TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, backoff.TimeUntilAllowed("bob"));
    }
}
