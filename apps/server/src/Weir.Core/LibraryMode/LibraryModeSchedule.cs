using Weir.Core.Jobs;

namespace Weir.Core.LibraryMode;

/// <summary>
/// When library mode's "Scheduled scan and clean" (<see cref="LibrarySettings.ScheduleEnabled"/>) next runs. There is no
/// second schedule to set: it runs once a day, inside the library's own schedule window (the same window the download
/// pipeline's work keeps to), so a library with no window drawn is scanned a day after the last scheduled scan and one
/// with a window waits for the window to open.
/// </summary>
/// <remarks>
/// Worked out from what is stored (when the last scheduled scan was asked for), never from a timer's memory, so the
/// Library screen and the timer that runs it agree, and a restart neither loses the day's run nor adds a second one.
/// </remarks>
public static class LibraryModeSchedule
{
    /// <summary>How long after one scheduled scan the next is due.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>The <c>trigger</c> a scheduled scan (and every clean it queues) carries in its job payload and Activity.</summary>
    public const string Trigger = "schedule";

    /// <summary>
    /// When the next scheduled run is due: a day after <paramref name="lastRunAt"/> (or straight away when there has
    /// never been one), moved to when <paramref name="window"/> next opens if it is shut then. The answer can be in the
    /// past, which means "due now"; keeping it rather than rounding it up to <paramref name="now"/> is what stops the
    /// daily run creeping later by one timer tick a day. Null when it will never run: the library is switched off, or
    /// its window has no open hour at all.
    /// </summary>
    public static DateTimeOffset? NextRunAt(DateTimeOffset? lastRunAt, LibraryAdmissionSnapshot window, string? timezoneName, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!window.Enabled)
        {
            return null;
        }

        var due = lastRunAt is { } last ? last + Interval : now;
        if (WorkAdmissionRules.LibraryWindowOpen(window, timezoneName, due))
        {
            return due;
        }

        if (ScheduleGrid.NextOpenSlot(EffectiveGrid(window), timezoneName, due) is not { } opens)
        {
            return null;
        }

        // A day/start/end window opens on the minute it names, which a quarter-hour grid slot can precede by up to
        // fourteen minutes; step to the first minute the window really is open.
        for (var minute = 0; minute < ScheduleGrid.SlotMinutes; minute++)
        {
            var candidate = opens.AddMinutes(minute);
            if (WorkAdmissionRules.LibraryWindowOpen(window, timezoneName, candidate))
            {
                return candidate;
            }
        }

        return opens;
    }

    /// <summary>The window as a grid: the drawn grid when there is one, else the day/start/end trio drawn as one.</summary>
    private static string EffectiveGrid(LibraryAdmissionSnapshot window)
    {
        var grid = (window.ScheduleGrid ?? string.Empty).Trim();
        if (grid.Length > 0)
        {
            return grid;
        }

        return window.ScheduleHoursLimited
            ? ScheduleGrid.FromDaysAndTimes(window.ScheduleDays, window.ScheduleStart, window.ScheduleEnd)
            : string.Empty;
    }
}
