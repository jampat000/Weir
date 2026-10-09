namespace Weir.Tray;

/// <summary>
/// How a check for an update or a download of one ended. <paramref name="Done"/> is true when the check found an
/// update or the download finished; <paramref name="Failure"/> says in plain words why not when something went wrong,
/// and is null when the answer was simply "nothing newer".
/// </summary>
readonly record struct UpdateOutcome(bool Done, string? Failure)
{
    internal static UpdateOutcome Succeeded { get; } = new(true, null);

    internal static UpdateOutcome NothingToDo { get; } = new(false, null);

    internal static UpdateOutcome Failed(string failure) => new(false, failure);
}
