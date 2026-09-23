using Weir.Core.Jobs;
using Weir.Core.LibraryMode;

namespace Weir.Core.Tests.LibraryMode;

/// <summary>Library mode's scheduled scan and clean runs once a day, inside the library's own window.</summary>
public sealed class LibraryModeScheduleTests
{
    private const string EveryDay = "Mon,Tue,Wed,Thu,Fri,Sat,Sun";

    // A Wednesday (2026-09-23), 10:00 UTC.
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static LibraryAdmissionSnapshot NoWindow(bool enabled = true) =>
        new(1, enabled, ScheduleEnabled: true, ScheduleGrid: null, ScheduleHoursLimited: false, null, null, null, 0);

    private static LibraryAdmissionSnapshot Grid(string grid) =>
        new(1, true, ScheduleEnabled: true, grid, ScheduleHoursLimited: false, null, null, null, 0);

    private static LibraryAdmissionSnapshot Hours(string days, string start, string end) =>
        new(1, true, ScheduleEnabled: true, ScheduleGrid: null, ScheduleHoursLimited: true, days, start, end, 0);

    [Fact]
    public void A_library_never_scanned_on_schedule_is_due_now()
    {
        Assert.Equal(Now, LibraryModeSchedule.NextRunAt(null, NoWindow(), "UTC", Now));
    }

    [Fact]
    public void The_next_run_is_a_day_after_the_last()
    {
        var last = Now.AddHours(-3);
        Assert.Equal(last.AddDays(1), LibraryModeSchedule.NextRunAt(last, NoWindow(), "UTC", Now));
    }

    [Fact]
    public void An_overdue_run_keeps_its_own_time_rather_than_now()
    {
        // The timer ticks a few seconds after the run was due; the next one is still a day after the due time.
        var last = Now.AddDays(-1).AddSeconds(-20);
        Assert.Equal(last.AddDays(1), LibraryModeSchedule.NextRunAt(last, NoWindow(), "UTC", Now));
    }

    [Fact]
    public void A_run_due_while_the_window_is_shut_waits_for_it_to_open()
    {
        // Open 02:00 to 04:00 every day; the last run was at 02:00 today, so tomorrow at 02:00 is inside the window.
        var grid = ScheduleGrid.FromDaysAndTimes(EveryDay, "02:00", "04:00");
        var last = new DateTimeOffset(2026, 9, 23, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(last.AddDays(1), LibraryModeSchedule.NextRunAt(last, Grid(grid), "UTC", Now));

        // Never run before and it is 10:00 now: the window next opens at 02:00 tomorrow.
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 2, 0, 0, TimeSpan.Zero), LibraryModeSchedule.NextRunAt(null, Grid(grid), "UTC", Now));
    }

    [Fact]
    public void A_day_and_hours_window_opens_on_the_minute_it_names()
    {
        // Saturdays from 01:10. Wednesday 10:00 now, so the next opening is Saturday 26 September at 01:10.
        Assert.Equal(
            new DateTimeOffset(2026, 9, 26, 1, 10, 0, TimeSpan.Zero),
            LibraryModeSchedule.NextRunAt(null, Hours("Sat", "01:10", "05:00"), "UTC", Now));
    }

    [Fact]
    public void The_window_is_read_in_the_instance_time_zone()
    {
        // 02:00 to 04:00 in Sydney (UTC+10 in September) is 16:00 to 18:00 UTC the day before.
        var grid = ScheduleGrid.FromDaysAndTimes(EveryDay, "02:00", "04:00");
        Assert.Equal(
            new DateTimeOffset(2026, 9, 23, 16, 0, 0, TimeSpan.Zero),
            LibraryModeSchedule.NextRunAt(null, Grid(grid), "Australia/Sydney", Now));
    }

    [Fact]
    public void A_window_with_no_open_hour_or_a_switched_off_library_never_runs()
    {
        Assert.Null(LibraryModeSchedule.NextRunAt(null, Grid(new string('0', ScheduleGrid.SlotsPerWeek)), "UTC", Now));
        Assert.Null(LibraryModeSchedule.NextRunAt(null, NoWindow(enabled: false), "UTC", Now));
    }
}
