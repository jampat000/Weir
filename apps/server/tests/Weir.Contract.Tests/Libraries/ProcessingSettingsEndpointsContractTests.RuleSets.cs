using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartBChecks;

namespace Weir.Contract.Tests.Libraries;

// Rule sets, reject support, the metadata provider and Direct Play devices.
public sealed partial class ProcessingSettingsEndpointsContractTests
{
    private const string RuleSets = $"{Api}/processing/rule-sets";
    private const string Metadata = $"{Api}/processing/metadata-provider";
    private const string Devices = $"{Api}/processing/direct-play/devices";

    // --- rule sets --------------------------------------------------------------------------------------

    [Fact]
    public async Task Rule_sets_need_a_session()
    {
        using var anonymous = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(RuleSets)).Status);
    }

    [Fact]
    public async Task Rule_set_round_trip()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var viewer = await SeededAccounts.SignInViewerAsync(Server);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostWithCsrfAsync(RuleSets, new JsonObject { ["name"] = "Viewer set" })).Status);

        var made = await admin.PostWithCsrfAsync(
            RuleSets,
            new JsonObject { ["name"] = "Contract round trip", ["primary_audio_lang"] = "eng", ["subtitle_mode"] = "remove_all" });
        Status(made, HttpStatusCode.Created);
        var row = made.Fields;
        HasKeys(
            row,
            "id",
            "name",
            "primary_audio_lang",
            "secondary_audio_lang",
            "tertiary_audio_lang",
            "default_audio_slot",
            "remove_commentary",
            "subtitle_mode",
            "subtitle_langs_csv",
            "preserve_forced_subs",
            "preserve_default_subs",
            "audio_preference_mode",
            "audio_sorters_json",
            "subtitle_sorters_json",
            "keep_original_language",
            "remove_images",
            "used_by_library_count",
            "updated_at");
        Assert.Equal("Contract round trip", (string)row["name"]!);
        Assert.Equal("eng", (string)row["primary_audio_lang"]!);
        Assert.Equal("remove_all", (string)row["subtitle_mode"]!);
        Assert.Equal(0, (int)row["used_by_library_count"]!);
        var id = (long)row["id"]!;

        var listed = (await viewer.GetAsync(RuleSets)).Elements.ToDictionary(item => (long)item!["id"]!, item => item!.AsObject());
        Assert.Equal("Contract round trip", (string)listed[id]["name"]!);

        var duplicate = await admin.PostWithCsrfAsync(RuleSets, new JsonObject { ["name"] = "Contract round trip" });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.Status);
        Assert.Contains("already exists", (string)duplicate.Fields["detail"]!, StringComparison.Ordinal);

        var updated = await admin.PutWithCsrfAsync(
            $"{RuleSets}/{id}",
            new JsonObject { ["name"] = "Contract round trip", ["primary_audio_lang"] = "jpn", ["remove_title"] = true });
        Status(updated, HttpStatusCode.OK);
        Assert.Equal("jpn", (string)updated.Fields["primary_audio_lang"]!);
        Assert.True((bool)updated.Fields["remove_title"]!);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutWithCsrfAsync($"{RuleSets}/{id}", new JsonObject { ["name"] = "x" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutWithCsrfAsync($"{RuleSets}/999999", new JsonObject { ["name"] = "Nope" })).Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteWithCsrfBodyAsync($"{RuleSets}/{id}")).Status);
        var deleted = await admin.DeleteWithCsrfBodyAsync($"{RuleSets}/{id}");
        Status(deleted, HttpStatusCode.NoContent);
        Assert.All((await admin.GetAsync(RuleSets)).Elements, item => Assert.NotEqual(id, (long)item!["id"]!));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteWithCsrfBodyAsync($"{RuleSets}/{id}")).Status);
    }

    [Fact]
    public async Task Rule_set_rejects_invalid_fields()
    {
        using var admin = await Server.CreateAdminClientAsync();

        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostWithCsrfAsync(RuleSets, new JsonObject { ["name"] = "Bad mode", ["subtitle_mode"] = "shred" })).Status);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity, (await admin.PostWithCsrfAsync(RuleSets, new JsonObject { ["name"] = string.Empty })).Status);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PostWithCsrfAsync(RuleSets, new JsonObject { ["name"] = "Extra", ["not_a_field"] = 1 })).Status);
    }

    // --- reject support ---------------------------------------------------------------------------------

    [Fact]
    public async Task Reject_support_for_a_viewer_and_unknown_connections()
    {
        using var viewer = await SeededAccounts.SignInViewerAsync(Server);

        var response = await viewer.GetAsync($"{Api}/processing/reject-support");
        Status(response, HttpStatusCode.OK);
        HasKeys(response.Fields, "available", "reason");
        var unknown = await viewer.GetAsync($"{Api}/processing/reject-support", ("connection_ids", 4242));

        Status(unknown, HttpStatusCode.OK);
        Assert.False((bool)unknown.Fields["available"]!);
        Assert.NotEmpty((string)unknown.Fields["reason"]!);
    }

    // --- metadata provider (deprecated: kept answering for older clients) -------------------------------

    [Fact]
    public async Task Metadata_provider_needs_a_session()
    {
        using var anonymous = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Metadata)).Status);
    }

    [Fact]
    public async Task Metadata_provider_always_describes_the_metadata_service()
    {
        using var viewer = await SeededAccounts.SignInViewerAsync(Server);

        var response = await viewer.GetAsync(Metadata);

        Status(response, HttpStatusCode.OK);
        var expected = new JsonObject
        {
            ["provider"] = "deluno-gateway",
            ["base_url"] = null,
            ["key_configured"] = false,
            ["known_providers"] = new JsonArray("deluno-gateway"),
            ["artwork_enabled"] = true,
        };
        Assert.True(JsonNode.DeepEquals(expected, response.Json), response.Text);
    }

    [Fact]
    public async Task A_metadata_provider_save_is_accepted_and_ignored()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var viewer = await SeededAccounts.SignInViewerAsync(Server);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PutWithCsrfAsync(Metadata, new JsonObject { ["provider"] = "tmdb", ["base_url"] = "https://x.example" })).Status);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PutWithCsrfAsync(Metadata, new JsonObject { ["provider"] = "tmdb", ["surprise"] = true })).Status);
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await admin.PutWithCsrfAsync(Metadata, new JsonObject { ["provider"] = string.Empty, ["artwork_enabled"] = "maybe" })).Status);

        var saved = await admin.PutWithCsrfAsync(
            Metadata,
            new JsonObject
            {
                ["provider"] = "tmdb",
                ["base_url"] = "https://tmdb.example/3",
                ["api_key"] = "secret-key",
                ["artwork_enabled"] = false,
            });

        Status(saved, HttpStatusCode.OK);
        Assert.True(JsonNode.DeepEquals(saved.Json, (await admin.GetAsync(Metadata)).Json), saved.Text);
        Assert.Equal("deluno-gateway", (string)saved.Fields["provider"]!);
        Assert.False((bool)saved.Fields["key_configured"]!);
        Assert.True((bool)saved.Fields["artwork_enabled"]!);
        Assert.DoesNotContain("secret-key", saved.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_metadata_provider_test_says_when_the_metadata_service_is_switched_off()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var viewer = await SeededAccounts.SignInViewerAsync(Server);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostWithCsrfAsync($"{Metadata}/test", new JsonObject())).Status);

        var tested = await admin.PostWithCsrfAsync(
            $"{Metadata}/test", new JsonObject { ["provider"] = "tmdb", ["artwork_enabled"] = true });

        Status(tested, HttpStatusCode.OK);
        Assert.Equal("not_configured", (string)tested.Fields["status"]!);
        Assert.NotEmpty((string)tested.Fields["detail"]!);
    }

    // --- Direct Play devices ----------------------------------------------------------------------------

    [Fact]
    public async Task Direct_play_devices_need_a_session()
    {
        using var anonymous = Server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Devices)).Status);
    }

    [Fact]
    public async Task Direct_play_devices_shape_and_permissions()
    {
        using var admin = await Server.CreateAdminClientAsync();
        using var viewer = await SeededAccounts.SignInViewerAsync(Server);

        var response = await viewer.GetAsync(Devices);

        Status(response, HttpStatusCode.OK);
        var body = response.Fields;
        Assert.False((bool)body["customised"]!);
        var devices = body["devices"]!.AsArray().Select(device => device!.AsObject()).ToList();
        Assert.Subset(
            devices.Select(device => (string)device["id"]!).ToHashSet(),
            new HashSet<string> { "apple_tv_4k", "lg_webos", "roku", "web_browser" });
        foreach (var device in devices)
        {
            HasKeys(device, "id", "name", "source", "note", "selected");
            Assert.StartsWith("https://", (string)device["source"]!, StringComparison.Ordinal);
        }

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await viewer.PutWithCsrfAsync(Devices, new JsonObject { ["selected"] = new JsonArray("roku") })).Status);
        var forged = await admin.RequestAsync(
            HttpMethod.Put, Devices, new JsonObject { ["csrf_token"] = "forged", ["selected"] = new JsonArray("roku") });
        Assert.Equal(HttpStatusCode.BadRequest, forged.Status);

        var saved = await admin.PutWithCsrfAsync(Devices, new JsonObject { ["selected"] = new JsonArray("roku") });
        Status(saved, HttpStatusCode.OK);
        Assert.Equal(["roku"], SelectedIds(saved));
        Assert.Equal(["roku"], SelectedIds(await viewer.GetAsync(Devices)));
        Assert.Equal(HttpStatusCode.OK, (await admin.PutWithCsrfAsync(Devices, new JsonObject { ["selected"] = new JsonArray() })).Status);
    }

    private static string[] SelectedIds(WeirResponse response) => [.. response.Fields["devices"]!.AsArray()
        .Where(device => (bool)device!["selected"]!)
        .Select(device => (string)device!["id"]!)];
}
