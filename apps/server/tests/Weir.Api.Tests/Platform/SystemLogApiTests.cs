using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Weir.Infrastructure.Logging;
using static Weir.Api.Tests.Platform.ApiTestClient;

namespace Weir.Api.Tests.Platform;

/// <summary>System › Logs over HTTP: one list of events, jobs and server lines, its filters, its cursor and its export.</summary>
public sealed class SystemLogApiTests
{
    private const string Log = "/api/v1/system/log";
    /// <summary>Only the day the rows are seeded on: signing in and starting the server write rows of their own, at the real time.</summary>
    private const string Window = "from=2026-05-09T00:00:00Z&to=2026-05-09T23:59:59Z";
    private const string SeededEventAt = "2026-05-09 10:00:00.000000";

    private static async Task<(WeirTestServer Server, ApiTestClient Client)> StartAsync()
    {
        var server = await StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();
        return (server, client);
    }

    private static IEnumerable<string> Levels(JsonNode page) => page["items"]!.AsArray().Select(item => item!["level"]!.GetValue<string>());

    private static async Task SeedAsync(WeirTestServer server)
    {
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO activity_events (created_at, event_type, module, title, detail, result, library_id) " +
            "VALUES ($at, 'library.scan_completed', 'library', 'Movies scanned', '{}', 'success', 1)",
            ("$at", SeededEventAt));
        await TestDatabase.ExecuteAsync(
            server,
            "INSERT INTO jobs (dedupe_key, job_kind, payload_json, status, attempt_count, max_attempts, last_error, created_at, updated_at) " +
            "VALUES ('seed:1', 'processing.file.remux_pass.v1', '{\"library_id\": 1}', 'failed', 3, 3, 'ffmpeg stopped', $at, '2026-05-09 10:30:00.000000')",
            ("$at", SeededEventAt));
        server.Services.GetRequiredService<WeirLogFile>().WriteLine(
            "{\"timestamp\":\"2026-05-09T10:45:00Z\",\"level\":\"WARNING\",\"logger\":\"weir.platform.suite_settings.backups\",\"message\":\"The backup folder is nearly full\"}");
    }

    [Fact]
    public async Task The_log_needs_a_session()
    {
        await using var server = await StartServerAsync();

        using var response = await new ApiTestClient(server).GetAsync(Log);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task One_list_holds_every_source_newest_first_with_the_counts_the_chips_show()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);

        var body = await Json(await client.GetAsync($"{Log}?{Window}"));

        var items = body["items"]!.AsArray();
        Assert.Equal(["server", "job", "event"], items.Select(item => item!["source"]!.GetValue<string>()));
        Assert.Equal(["warning", "error", "success"], items.Select(item => item!["level"]!.GetValue<string>()));
        Assert.Equal(["backups", "processing", "scans"], items.Select(item => item!["category"]!.GetValue<string>()));
        Assert.Null(body["next_cursor"]);
        Assert.Equal(3, body["total"]!.GetValue<int>());
        Assert.Equal(1, body["counts"]!["source"]!["job"]!.GetValue<int>());
        Assert.Equal(1, body["counts"]!["level"]!["error"]!.GetValue<int>());
        Assert.Equal(1, body["counts"]!["category"]!["backups"]!.GetValue<int>());
        Assert.Equal(0, body["counts"]!["category"]!["sign_in"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_row_carries_the_record_of_its_source_and_the_other_two_are_null()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);

        var items = (await Json(await client.GetAsync($"{Log}?{Window}")))["items"]!.AsArray();

        var job = items[1]!;
        Assert.Equal("ffmpeg stopped", job["job"]!["last_error"]!.GetValue<string>());
        Assert.Null(job["event"]);
        Assert.Null(job["server"]);
        Assert.Equal("Process a media file · attempt 3 of 3", job["detail"]!.GetValue<string>());
        Assert.Equal(1, job["workflow"]!["id"]!.GetValue<int>());
        Assert.False(string.IsNullOrEmpty(job["workflow"]!["name"]!.GetValue<string>()));
        Assert.Equal("weir.platform.suite_settings.backups", items[0]!["server"]!["logger"]!.GetValue<string>());
        Assert.Equal("library.scan_completed", items[2]!["event"]!["event_type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Filters_apply_together_and_a_value_may_be_repeated_or_separated_by_commas()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);

        var errors = await Json(await client.GetAsync($"{Log}?{Window}&level=error&source=job&source=event"));
        var either = await Json(await client.GetAsync($"{Log}?{Window}&level=error,warning"));
        var searched = await Json(await client.GetAsync($"{Log}?{Window}&q=backup%20folder"));
        var workflow = await Json(await client.GetAsync($"{Log}?{Window}&workflow=1"));

        Assert.Equal(["job"], errors["items"]!.AsArray().Select(item => item!["source"]!.GetValue<string>()));
        Assert.Equal(2, either["items"]!.AsArray().Count);
        Assert.Equal(["server"], searched["items"]!.AsArray().Select(item => item!["source"]!.GetValue<string>()));
        Assert.Equal(["job", "event"], workflow["items"]!.AsArray().Select(item => item!["source"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_cursor_continues_where_the_page_ended()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);

        var first = await Json(await client.GetAsync($"{Log}?{Window}&limit=2"));
        var second = await Json(await client.GetAsync($"{Log}?{Window}&limit=2&cursor={Uri.EscapeDataString(first["next_cursor"]!.GetValue<string>())}"));

        Assert.Equal(2, first["items"]!.AsArray().Count);
        Assert.Equal(["event"], second["items"]!.AsArray().Select(item => item!["source"]!.GetValue<string>()));
        Assert.Null(second["next_cursor"]);
        Assert.Equal(3, second["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task The_list_can_be_sorted_by_level_in_either_direction_and_a_cursor_keeps_to_that_sort()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);

        var ascending = await Json(await client.GetAsync($"{Log}?{Window}&sort=level&direction=asc&limit=2"));
        var descending = await Json(await client.GetAsync($"{Log}?{Window}&sort=level&direction=desc"));
        var next = await Json(await client.GetAsync($"{Log}?{Window}&sort=level&direction=asc&limit=2&cursor={Uri.EscapeDataString(ascending["next_cursor"]!.GetValue<string>())}"));

        Assert.Equal(["error", "warning"], Levels(ascending));
        Assert.Equal(["success", "warning", "error"], Levels(descending));
        Assert.Equal(["success"], Levels(next));
        Assert.Null(next["next_cursor"]);
    }

    [Theory]
    [InlineData("sort=level&direction=desc")]
    [InlineData("sort=category&direction=asc")]
    [InlineData("sort=time&direction=desc")]
    public async Task A_cursor_made_for_another_sort_or_direction_is_refused(string otherOrder)
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);
        var first = await Json(await client.GetAsync($"{Log}?{Window}&sort=level&direction=asc&limit=2"));

        using var response = await client.GetAsync($"{Log}?{Window}&{otherOrder}&cursor={Uri.EscapeDataString(first["next_cursor"]!.GetValue<string>())}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData("source=disk")]
    [InlineData("level=fatal")]
    [InlineData("category=misc")]
    [InlineData("status=stuck")]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("workflow=0")]
    [InlineData("from=yesterday")]
    [InlineData("cursor=nonsense")]
    [InlineData("sort=message")]
    [InlineData("sort=")]
    [InlineData("direction=sideways")]
    [InlineData("result=great")]
    [InlineData("has_exception=maybe")]
    public async Task A_filter_the_log_does_not_understand_is_refused_with_what_was_wrong(string query)
    {
        var (server, client) = await StartAsync();
        await using var _ = server;

        using var response = await client.GetAsync($"{Log}?{query}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.NotEmpty((await Json(response))["detail"]!.AsArray());
    }

    [Fact]
    public async Task The_export_is_a_spreadsheet_or_the_rows_for_the_same_filters()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);

        using var csv = await client.GetAsync($"{Log}/export?{Window}&level=error,warning");
        using var json = await client.GetAsync($"{Log}/export?{Window}&format=json&source=event");

        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Contains("attachment; filename=\"weir-log-", Header(csv, "Content-Disposition"), StringComparison.Ordinal);
        Assert.Equal("2", Header(csv, "X-Weir-Export-Rows"));
        var lines = (await csv.Content.ReadAsStringAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("time,source,level,category,workflow,title,detail", lines[0]);
        Assert.Contains("job,error,processing", lines[2], StringComparison.Ordinal);
        Assert.Contains("ffmpeg stopped", lines[2], StringComparison.Ordinal);
        var rows = JsonNode.Parse(await json.Content.ReadAsStringAsync())!.AsArray();
        Assert.Equal("Movies scanned", rows.Single()!["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_export_lists_the_rows_in_the_order_the_screen_shows_them()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;
        await SeedAsync(server);

        using var json = await client.GetAsync($"{Log}/export?{Window}&format=json&sort=level&direction=asc");

        var rows = JsonNode.Parse(await json.Content.ReadAsStringAsync())!.AsArray();
        Assert.Equal(["error", "warning", "success"], rows.Select(row => row!["level"]!.GetValue<string>()));
    }

    [Fact]
    public async Task An_export_in_a_format_that_is_not_offered_is_refused()
    {
        var (server, client) = await StartAsync();
        await using var _ = server;

        using var response = await client.GetAsync($"{Log}/export?format=xml");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
