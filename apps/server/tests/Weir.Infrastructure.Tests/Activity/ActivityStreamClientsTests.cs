using Weir.Infrastructure.Activity;

namespace Weir.Infrastructure.Tests.Activity;

/// <summary>How many browsers hold the live Activity stream open.</summary>
public sealed class ActivityStreamClientsTests
{
    [Fact]
    public void Each_open_stream_counts_until_it_is_closed()
    {
        var clients = new ActivityStreamClients();

        var first = clients.Open();
        var second = clients.Open();
        Assert.Equal(2, clients.Count);

        first.Dispose();
        Assert.Equal(1, clients.Count);

        second.Dispose();
        Assert.Equal(0, clients.Count);
    }

    [Fact]
    public void Closing_the_same_stream_twice_counts_it_out_once()
    {
        var clients = new ActivityStreamClients();
        using var staying = clients.Open();
        var leaving = clients.Open();

        leaving.Dispose();
        leaving.Dispose();

        Assert.Equal(1, clients.Count);
    }
}
