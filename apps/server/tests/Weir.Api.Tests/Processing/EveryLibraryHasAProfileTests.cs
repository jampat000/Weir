using System.Net;
using Weir.Api.Tests.Platform;

namespace Weir.Api.Tests.Processing;

/// <summary>
/// Every library has a profile, because "Use scope defaults" would mean one thing for new downloads and another for
/// library cleaning. A library left without one is given the one it was using when Weir starts, and a library made
/// without one gets its kind's.
/// </summary>
public sealed class EveryLibraryHasAProfileTests
{
    [Fact]
    public async Task Every_library_has_a_profile_and_a_new_one_gets_its_kinds()
    {
        await using var server = await ApiTestClient.StartServerAsync();
        await TestDatabase.SeedAdminAsync(server);
        var client = new ApiTestClient(server);
        await client.SignInAsync();

        using var listed = await client.GetAsync("/api/v1/processing/libraries");
        var libraries = (await ApiTestClient.Json(listed)).AsArray();
        Assert.NotEmpty(libraries);
        Assert.All(libraries, library => Assert.NotNull(library!["rule_set_id"]));
        var movies = libraries.First(l => l!["media_type"]!.GetValue<string>() == "movie")!;

        using var created = await client.PostAsync(
            "/api/v1/processing/libraries",
            new { csrf_token = await client.CsrfAsync(), name = "4K Movies", media_type = "movie" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Assert.Equal(
            movies["rule_set_id"]!.GetValue<long>(),
            (await ApiTestClient.Json(created))["rule_set_id"]!.GetValue<long>());
    }
}
