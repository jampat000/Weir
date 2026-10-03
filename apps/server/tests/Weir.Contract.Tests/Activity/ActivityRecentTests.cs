using System.Globalization;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Activity;

/// <summary>
/// Counting, time filters and paging on <c>/api/v1/activity/recent</c> (#543). Two things every test here works around:
/// seeding restarts the server on a new port, so each client is created after the seeding block; and the server's
/// own retention pass prunes events older than 90 days against real wall-clock time, so seeded rows are dated close
/// to now rather than at a fixed past date the pass could delete out from under the test.
/// </summary>
[ContractArea("activity")]
public sealed class ActivityRecentTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string Recent = $"{WeirClient.Api}/activity/recent";

    private WeirServer Server => fixture.Server;

    [Fact]
    public async Task Recent_total_and_has_more_count_every_matching_row_with_no_filter()
    {
        // With no filter the total counts every row (a count query without its FROM answers 1).
        var now = DateTime.UtcNow;
        await using (var database = await Server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM activity_events");
            for (var row = 0; row < 5; row++)
            {
                ActivityRows.InsertEvent(database.Connection, "a.event", "processing", $"row {row}", createdAt: now);
            }
        }

        using var client = Server.CreateClient();
        await client.EnsureAdminAsync();

        // A page that holds everything tells us the real total without relying on a fix (max(total, len(items))
        // already hides the bug when nothing is truncated). It also counts the admin sign-in's own event,
        // however many that turns out to be, without hard-coding it.
        var wholePage = (await client.GetAsync(Recent, ("limit", 100))).Fields;
        var total = wholePage["items"]!.AsArray().Count;
        Assert.True(total >= 5);
        Assert.Equal(total, (int)wholePage["total"]!);
        Assert.False((bool)wholePage["has_more"]!);

        var smallPage = (await client.GetAsync(Recent, ("limit", 2))).Fields;
        Assert.Equal(total, (int)smallPage["total"]!);
        Assert.True((bool)smallPage["has_more"]!);
        Assert.Equal(2, smallPage["items"]!.AsArray().Count);
    }

    [Fact]
    public async Task Recent_date_filters_normalize_stored_timestamps_and_honor_offsets()
    {
        // A query offset must be honored, and a stored exact-second row must not be excluded.
        var now = DateTime.UtcNow;
        var exactSecond = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        await using (var database = await Server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM activity_events");
            // A whole second: SeedSql.UtcText writes this with no fractional part.
            ActivityRows.InsertEvent(database.Connection, "a.exact", "custom", "exact second", createdAt: exactSecond);
        }

        using var client = Server.CreateClient();
        await client.EnsureAdminAsync();

        // The naive filter names the same second the row is stored at, just formatted without a fraction of its own:
        // comparing raw text against "...05.000000" would wrongly exclude it.
        var naiveBoundary = exactSecond.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        Assert.Equal(["exact second"], await CustomTitlesAsync(client, ("date_from", naiveBoundary)));
        Assert.Equal(["exact second"], await CustomTitlesAsync(client, ("date_to", naiveBoundary)));

        // The same instant, named with a +02:00 offset: an ignored offset would compare the shifted wall-clock
        // digits against the stored UTC ones and wrongly exclude the row.
        var sameInstantOffset = OffsetText(exactSecond, TimeSpan.FromHours(2));
        Assert.Equal(["exact second"], await CustomTitlesAsync(client, ("date_from", sameInstantOffset)));

        // One second later in the same offset: now past the row, in either timezone.
        var pastIt = OffsetText(exactSecond + TimeSpan.FromSeconds(1), TimeSpan.FromHours(2));
        Assert.Empty(await CustomTitlesAsync(client, ("date_from", pastIt)));
    }

    [Fact]
    public async Task Recent_paging_by_before_id_never_skips_or_repeats_a_tied_or_out_of_order_row()
    {
        // Ordered by (created_at DESC, id DESC); before_id pages by that same key, not by id alone.
        var now = DateTime.UtcNow;
        await using (var database = await Server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM activity_events");
            // Row 2 is chronologically the oldest despite its small id; rows 1 and 3 tie on created_at. Plain
            // "id < before_id" paging loses that ordering: it can repeat row 4 on a later page (small id, but
            // already returned) or skip rows entirely.
            ActivityRows.InsertEvent(database.Connection, "a.1", "custom", "A", createdAt: now - TimeSpan.FromSeconds(1));
            ActivityRows.InsertEvent(database.Connection, "a.2", "custom", "B", createdAt: now - TimeSpan.FromSeconds(6));
            ActivityRows.InsertEvent(database.Connection, "a.3", "custom", "C", createdAt: now - TimeSpan.FromSeconds(1));
            ActivityRows.InsertEvent(database.Connection, "a.4", "custom", "D", createdAt: now - TimeSpan.FromSeconds(10));
        }

        using var client = Server.CreateClient();
        await client.EnsureAdminAsync();

        var first = (await client.GetAsync(Recent, ("module", "custom"), ("limit", 2))).Fields["items"]!.AsArray();
        // The tie is broken by id descending: 3 before 1.
        Assert.Equal(["C", "A"], Titles(first));

        var beforeId = (long)first[^1]!["id"]!;
        var second = (await client.GetAsync(Recent, ("module", "custom"), ("limit", 2), ("before_id", beforeId))).Fields["items"]!.AsArray();
        // Never A again, never loses B or D.
        Assert.Equal(["B", "D"], Titles(second));

        var seenIds = first.Concat(second).Select(item => (long)item!["id"]!).ToList();
        Assert.Equal(4, seenIds.Count);
        Assert.Equal(4, seenIds.Distinct().Count());
    }

    private static string[] Titles(JsonArray items) => items.Select(item => (string)item!["title"]!).ToArray();

    private static async Task<string[]> CustomTitlesAsync(WeirClient client, (string Name, object Value) filter)
    {
        var response = await client.GetAsync(Recent, ("module", "custom"), filter);
        return Titles(response.Fields["items"]!.AsArray());
    }

    /// <summary>The instant (UTC) written in the wall clock of a timezone <paramref name="offset"/> east of UTC.</summary>
    private static string OffsetText(DateTime instant, TimeSpan offset)
    {
        var local = instant + offset;
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var magnitude = offset.Duration();
        return $"{local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)}{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}";
    }
}
