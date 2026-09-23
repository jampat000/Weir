using Weir.Core.Library;
using Weir.Core.Rules;
using Weir.Infrastructure.Library;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Library;

/// <summary>
/// The <see cref="IRemovedTrackStore"/> (#509): the durable <c>removed_tracks</c>-table-backed
/// store. <see cref="FileLogRemovedTrackStore"/> only reads and writes the
/// table; converting legacy free-text <c>removed_audio</c>/<c>removed_subtitles</c> lists is #557's one-time
/// migration (see <c>Migrations/RemovedTracksMigrationTests</c>).
/// </summary>
public sealed class RemovedTrackStoreTests
{
    private static RemovedTrackRecord Track(string lang, RemovedTrackType type = RemovedTrackType.Audio) => new()
    {
        Language = lang,
        Type = type,
        Codec = "aac",
        Reason = "not selected",
    };

    [Fact]
    public async Task A_record_survives_a_new_store_over_the_same_database()
    {
        using var fixture = new StoreFixture();
        var key = new RemovedTrackFileKey(1, "Movies/A.mkv");
        await new FileLogRemovedTrackStore(fixture.Database, fixture.Clock).RecordAsync(key, [Track("jpn")]);

        var afterRestart = new FileLogRemovedTrackStore(fixture.Database, fixture.Clock);

        Assert.Equal(["jpn"], (await afterRestart.GetAsync(key)).Select(t => t.Language));
    }

    [Fact]
    public async Task Table_backed_store_round_trips_through_removed_tracks()
    {
        using var fixture = new StoreFixture();
        var store = new FileLogRemovedTrackStore(fixture.Database, fixture.Clock);
        var key = new RemovedTrackFileKey(1, "Movies/A.mkv");

        Assert.Empty(await store.GetAsync(key));

        await store.RecordAsync(key, [Track("jpn"), Track("spa", RemovedTrackType.Subtitle)]);

        var tracks = await store.GetAsync(key);
        Assert.Equal(2, tracks.Count);
        Assert.Equal("jpn", tracks[0].Language);
        Assert.Equal(RemovedTrackType.Audio, tracks[0].Type);
        Assert.Equal("aac", tracks[0].Codec);
        Assert.Equal("spa", tracks[1].Language);
        Assert.Equal(RemovedTrackType.Subtitle, tracks[1].Type);

        var all = await store.GetAllAsync();
        var only = Assert.Single(all);
        Assert.Equal(key, only.Key);
    }

    [Fact]
    public async Task Table_backed_store_replaces_on_a_second_recording()
    {
        using var fixture = new StoreFixture();
        var store = new FileLogRemovedTrackStore(fixture.Database, fixture.Clock);
        var key = new RemovedTrackFileKey(null, "Movies/B.mkv");

        await store.RecordAsync(key, [Track("jpn")]);
        fixture.Clock.Set(fixture.Clock.GetUtcNow().AddMinutes(1));
        await store.RecordAsync(key, [Track("fre")]);

        var tracks = await store.GetAsync(key);
        var only = Assert.Single(tracks);
        Assert.Equal("fre", only.Language);
    }
}
