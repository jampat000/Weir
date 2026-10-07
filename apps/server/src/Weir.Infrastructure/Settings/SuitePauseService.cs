using System.Globalization;
using Weir.Core.Activity;
using Weir.Core.Settings;
using Weir.Core.Time;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Settings;

/// <summary>
/// The suite-wide pause: every way it changes (someone pausing or resuming, a timed pause running out) is written to the
/// <c>suite_settings</c> row and to Activity in the same unit of work, so what Weir did can always be read back, with who
/// and when.
/// </summary>
public sealed class SuitePauseService
{
    private readonly SuiteSettingsStore _settings;
    private readonly ActivityStore _activity;

    public SuitePauseService(SuiteSettingsStore settings, ActivityStore activity)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
    }

    /// <summary>The pause as it stands at <paramref name="now"/>; a timed pause that has run out is lifted, and said so, first.</summary>
    public async Task<PauseState> CurrentAsync(UnitOfWork uow, Timestamp now)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var row = await _settings.EnsureAsync(uow).ConfigureAwait(false);
        return await LapseAsync(uow, row, now).ConfigureAwait(false);
    }

    /// <summary>
    /// Pauses (for <paramref name="minutes"/>, or until resumed when none) or resumes, on behalf of <paramref name="by"/>. A
    /// request that says nothing about the length (<paramref name="keepEnd"/>) leaves a pause that is already running as it is,
    /// whatever else it changes; an explicit "no length" makes the pause last until it is resumed.
    /// </summary>
    public async Task<PauseState> ChangeAsync(UnitOfWork uow, bool paused, long? minutes, bool keepEnd, bool scanWhilePaused, Timestamp now, string by)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        var row = await _settings.EnsureAsync(uow).ConfigureAwait(false);
        var before = await LapseAsync(uow, row, now).ConfigureAwait(false);
        row = row with { ProcessingPaused = before.Paused, ProcessingPausedUntil = before.Paused ? before.PausedUntil : null };

        Timestamp? until = !paused ? null
            : keepEnd && before.Paused ? before.PausedUntil
            : minutes is { } length ? Timestamp.FromUtc(now.AsUtc.AddMinutes((double)length)) : null;
        await _settings.UpdateAsync(uow, row, row with { ProcessingPaused = paused, ScanWhilePaused = scanWhilePaused, ProcessingPausedUntil = until }).ConfigureAwait(false);
        var after = PauseState.Resolve(paused, until, scanWhilePaused, now.AsUtc);

        if (after.Paused && (!before.Paused || before.PausedUntil?.AsUtc != after.PausedUntil?.AsUtc || before.ScanWhilePaused != after.ScanWhilePaused))
        {
            await _activity.RecordAsync(uow, ActivityEventTypes.SystemProcessingPaused, "system", "Processing paused", PausedDetail(after, by)).ConfigureAwait(false);
        }
        else if (!after.Paused && before.Paused)
        {
            await _activity.RecordAsync(uow, ActivityEventTypes.SystemProcessingResumed, "system", "Processing resumed", $"Processing was resumed by {by}.").ConfigureAwait(false);
        }

        return after;
    }

    private async Task<PauseState> LapseAsync(UnitOfWork uow, SuiteSettingsRecord row, Timestamp now)
    {
        var state = PauseState.Resolve(row, now.AsUtc);
        if (!state.Expired)
        {
            return state;
        }

        await _settings.UpdateAsync(uow, row, row with { ProcessingPaused = false, ProcessingPausedUntil = null }).ConfigureAwait(false);
        await _activity.RecordAsync(
            uow,
            ActivityEventTypes.SystemProcessingResumed,
            "system",
            "Processing resumed",
            $"The pause ran out at {UtcText(state.PausedUntil)}, so processing was resumed.").ConfigureAwait(false);
        return state with { Expired = false };
    }

    private static string PausedDetail(PauseState state, string by)
    {
        var length = state.PausedUntil is { } until ? $"until {UtcText(until)}" : "until you resume";
        var looking = state.ScanWhilePaused
            ? "Weir keeps looking for new files and starts nothing."
            : "Weir does not look for new files either.";
        return $"Processing was paused {length} by {by}. {looking}";
    }

    private static string UtcText(Timestamp? at) =>
        at is { } value ? value.AsUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "an unknown time";
}
