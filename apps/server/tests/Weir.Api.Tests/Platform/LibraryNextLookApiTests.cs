using Microsoft.Extensions.DependencyInjection;
using Weir.Infrastructure.Jobs;

namespace Weir.Api.Tests.Platform;

/// <summary>
/// <c>GET /processing/libraries</c> says when Weir next looks at each library's watched folder, so Processing can count an
/// arriving file down to a real moment instead of showing "…".
/// </summary>
public sealed class LibraryNextLookApiTests
{
    [Fact]
    public async Task Each_library_reports_its_next_look_and_a_booked_look_comes_first()
    {
        var server = await ApiTestClient.StartServerAsync();
        await using var disposeServer = server;
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var listed = await client.GetAsync("/api/v1/processing/libraries");
        var library = (await ApiTestClient.Json(listed)).AsArray()[0]!;
        var id = library["id"]!.GetValue<long>();

        var looks = server.Services.GetRequiredService<ScanWakeups>();
        var periodic = DateTimeOffset.UtcNow.AddMinutes(5);
        looks.RecordNextPeriodic(id, periodic);
        var booked = DateTimeOffset.UtcNow.AddSeconds(20);
        looks.Request(id, booked);

        using var one = await client.GetAsync($"/api/v1/processing/libraries/{id}");
        var nextLook = (await ApiTestClient.Json(one))["next_look_at"]!.GetValue<string>();

        Assert.InRange(DateTimeOffset.Parse(nextLook, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(), booked.AddSeconds(-1), booked.AddSeconds(1));
    }
}
