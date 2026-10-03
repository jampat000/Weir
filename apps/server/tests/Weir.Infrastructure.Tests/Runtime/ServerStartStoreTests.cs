using Weir.Infrastructure.Runtime;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.Jobs;

namespace Weir.Infrastructure.Tests.Runtime;

/// <summary>How often Weir restarted: each start is kept, and the first start of an install is not a restart.</summary>
public sealed class ServerStartStoreTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly JobsTestDatabase _db = new();
    private readonly ServerStartStore _starts = new();

    public void Dispose() => _db.Dispose();

    private async Task StartedAsync(DateTimeOffset at)
    {
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);
        await _starts.RecordStartAsync(uow, at);
        await uow.CommitAsync();
    }

    private async Task<long> RestartsSinceAsync(DateTimeOffset since)
    {
        await using var uow = await UnitOfWork.OpenAsync(_db.Database);
        return await _starts.RestartsSinceAsync(uow, since);
    }

    [Fact]
    public async Task The_first_start_of_an_install_is_not_a_restart()
    {
        await StartedAsync(Noon);

        Assert.Equal(0, await RestartsSinceAsync(Noon - Week));
    }

    [Fact]
    public async Task Every_later_start_in_the_window_is_a_restart()
    {
        await StartedAsync(Noon.AddDays(-10));
        await StartedAsync(Noon.AddDays(-3));
        await StartedAsync(Noon.AddDays(-1));
        await StartedAsync(Noon);

        Assert.Equal(3, await RestartsSinceAsync(Noon - Week));
    }

    [Fact]
    public async Task A_start_before_the_window_is_not_counted()
    {
        await StartedAsync(Noon.AddDays(-20));
        await StartedAsync(Noon.AddDays(-10));
        await StartedAsync(Noon);

        Assert.Equal(1, await RestartsSinceAsync(Noon - Week));
    }

    [Fact]
    public async Task A_start_is_forgotten_after_a_year()
    {
        await StartedAsync(Noon.AddDays(-400));
        await StartedAsync(Noon);

        Assert.Equal(1, _db.Count("SELECT COUNT(*) FROM server_starts"));
    }
}
