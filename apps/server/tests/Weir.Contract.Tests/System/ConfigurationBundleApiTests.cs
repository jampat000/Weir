using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>The configuration bundle (<c>/suite/configuration-bundle</c>): access, its one address, and round trips.</summary>
[ContractArea("system")]
public sealed class ConfigurationBundleApiTests(SystemPartASeededServerFixture fixture) : IClassFixture<SystemPartASeededServerFixture>
{
    private const string Bundle = $"{WeirClient.Api}/suite/configuration-bundle";

    [Fact]
    public async Task Configuration_bundle_get_requires_operator()
    {
        using var viewer = await SystemPartAHelpers.SignedInViewerAsync(fixture.Server);
        var response = await viewer.GetAsync(Bundle);
        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
    }

    /// <summary>
    /// One address per handler: <c>/suite/settings/configuration-bundle</c> and the <c>/system/...</c> bundle
    /// and backup paths are not served, so nothing comes to depend on a second address.
    /// </summary>
    [Fact]
    public async Task The_retired_bundle_url_aliases_are_not_served()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        foreach (var path in new[]
        {
            $"{WeirClient.Api}/suite/settings/configuration-bundle",
            $"{WeirClient.Api}/system/suite-configuration-bundle",
            $"{WeirClient.Api}/system/suite-configuration-backups",
        })
        {
            Assert.True((await admin.GetAsync(path)).Status == HttpStatusCode.NotFound, path);
        }
    }

    [Fact]
    public async Task Configuration_bundle_round_trip_ignores_the_product_name()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var r0 = await admin.GetAsync(Bundle);
        Assert.True(r0.Status == HttpStatusCode.OK, r0.ToString());
        var bundle = r0.Fields;
        Assert.Equal(4, (int)bundle["format_version"]!);
        Assert.True(bundle.ContainsKey("suite_settings"));
        Assert.True(bundle.ContainsKey("arr_library_operator_settings"));

        var changed = bundle.DeepClone().AsObject();
        changed["suite_settings"]!["product_display_name"] = "Bundle Restore Test";

        var put = await admin.PutWithCsrfAsync(Bundle, new JsonObject { ["bundle"] = changed });
        Assert.True(put.Status == HttpStatusCode.OK, put.ToString());
        // Weir is named after its machine, so a backup's product name is read and dropped.
        Assert.Equal(
            (string?)bundle["suite_settings"]!["product_display_name"],
            (string?)put.Fields["suite_settings"]!["product_display_name"]);

        var restore = await admin.PutWithCsrfAsync(Bundle, new JsonObject { ["bundle"] = bundle.DeepClone() });
        Assert.True(restore.Status == HttpStatusCode.OK, restore.ToString());
    }

    /// <summary>Pruner moved to Deluno (#473): exports drop its sections, older backups still restore.</summary>
    [Fact]
    public async Task Configuration_bundle_no_longer_carries_pruner_and_ignores_it_on_restore()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var r0 = await admin.GetAsync(Bundle);
        Assert.True(r0.Status == HttpStatusCode.OK, r0.ToString());
        var bundle = r0.Fields;
        Assert.DoesNotContain(bundle.Select(field => field.Key), key => key.StartsWith("pruner_", StringComparison.Ordinal));

        var older = bundle.DeepClone().AsObject();
        older["suite_settings"]!["product_display_name"] = "Restored From Older Backup";
        older["pruner_server_instances"] = new JsonArray(new JsonObject
        {
            ["id"] = 1,
            ["provider"] = "plex",
            ["display_name"] = "Living room",
        });
        older["pruner_scope_settings"] = new JsonArray(new JsonObject
        {
            ["id"] = 1,
            ["server_instance_id"] = 1,
            ["media_scope"] = "movies",
        });

        var put = await admin.PutWithCsrfAsync(Bundle, new JsonObject { ["bundle"] = older });
        Assert.True(put.Status == HttpStatusCode.OK, put.ToString());
        Assert.Equal(
            (string?)bundle["suite_settings"]!["product_display_name"],
            (string?)put.Fields["suite_settings"]!["product_display_name"]);

        var restore = await admin.PutWithCsrfAsync(Bundle, new JsonObject { ["bundle"] = bundle.DeepClone() });
        Assert.True(restore.Status == HttpStatusCode.OK, restore.ToString());
    }

    [Fact]
    public async Task Configuration_bundle_put_rejects_bad_version()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var r0 = await admin.GetAsync(Bundle);
        Assert.Equal(HttpStatusCode.OK, r0.Status);
        var bundle = r0.Fields;
        bundle["format_version"] = 999;

        var put = await admin.PutWithCsrfAsync(Bundle, new JsonObject { ["bundle"] = bundle.DeepClone() });
        Assert.Equal(HttpStatusCode.BadRequest, put.Status);
    }

    /// <summary>
    /// A bundle whose library rows would fail POST /processing/libraries's own checks is refused as a
    /// whole, naming the offending library, and leaves the library table untouched (#723).
    /// </summary>
    [Fact]
    public async Task Configuration_bundle_restore_validates_every_library_row()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var r0 = await admin.GetAsync(Bundle);
        Assert.True(r0.Status == HttpStatusCode.OK, r0.ToString());
        var bundle = r0.Fields;
        var before = (await admin.GetAsync($"{WeirClient.Api}/processing/libraries")).Elements;

        var bad = bundle.DeepClone().AsObject();
        var movies = bad["libraries"]!.AsArray().First(row => (string?)row!["name"] == "Movies")!;
        movies["watched_folder"] = "/srv/movies/in";
        movies["output_folder"] = "";

        var put = await admin.PutWithCsrfAsync(Bundle, new JsonObject { ["bundle"] = bad });
        Assert.True(put.Status == HttpStatusCode.BadRequest, put.ToString());
        var detail = (string)put.Fields["detail"]!;
        Assert.Contains("Movies", detail, StringComparison.Ordinal);
        Assert.Contains("output folder", detail, StringComparison.Ordinal);

        var after = (await admin.GetAsync($"{WeirClient.Api}/processing/libraries")).Elements;
        Assert.Equal(
            before.Select(row => (string?)row!["watched_folder"]),
            after.Select(row => (string?)row!["watched_folder"]));
    }

    [Fact]
    public async Task Configuration_backup_list_shape()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        var response = await admin.GetAsync($"{WeirClient.Api}/suite/configuration-backups");
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        Assert.True(response.Fields.ContainsKey("directory"));
        Assert.IsType<JsonArray>(response.Fields["items"]);
    }
}
