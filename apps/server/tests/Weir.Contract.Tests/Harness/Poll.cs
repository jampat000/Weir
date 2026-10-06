using Xunit.Sdk;

namespace Weir.Contract.Tests.Harness;

/// <summary>Waiting for something the server does on its own schedule, with a deadline instead of a fixed pause.</summary>
public static class Poll
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    /// <summary>Calls <paramref name="probe"/> until it returns a value, and returns that value; fails with a sentence on timeout.</summary>
    public static async Task<T> UntilAsync<T>(Func<Task<T?>> probe, string what, TimeSpan? timeout = null)
        where T : class
    {
        var limit = timeout ?? DefaultTimeout;
        var deadline = DateTime.UtcNow + limit;
        string last = "nothing";
        while (true)
        {
            try
            {
                var value = await probe();
                if (value is not null)
                {
                    return value;
                }

                last = "no result yet";
            }
            catch (XunitException failure)
            {
                last = failure.Message;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new XunitException($"Timed out after {limit.TotalSeconds:0}s waiting for {what}; last saw: {last}");
            }

            await Task.Delay(Interval);
        }
    }

    /// <summary>Calls <paramref name="condition"/> until it holds.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, string what, TimeSpan? timeout = null) =>
        await UntilAsync(async () => await condition() ? "done" : null, what, timeout);
}
