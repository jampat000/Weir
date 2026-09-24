namespace Weir.Infrastructure.Jobs;

/// <summary>Job completion and failure notifications; the notifications area owns delivery.</summary>
public interface IJobNotifications
{
    /// <summary>
    /// <paramref name="eventKind"/> is <c>completed</c> or <c>failed</c>. <paramref name="willRetry"/>
    /// (#540 item 6) says whether another attempt follows a <c>failed</c> event, so the wording says so
    /// instead of always claiming retries are exhausted; it is ignored for <c>completed</c>. Must not throw.
    /// </summary>
    void Dispatch(string moduleName, string eventKind, long jobId, string jobKind, bool willRetry = false);
}

/// <summary>Sends nothing; used when no notification sender is registered.</summary>
public sealed class NoJobNotifications : IJobNotifications
{
    public void Dispatch(string moduleName, string eventKind, long jobId, string jobKind, bool willRetry = false)
    {
    }
}
