using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Direct Play: choosing devices changes only the badge a file shows.</summary>
[ContractArea("libraries")]
public sealed class DirectPlayTests
{
    private const string Devices = Api + "/processing/direct-play/devices";
    private const string Files = Api + "/processing/files";

    [Fact]
    public async Task Choosing_devices_changes_only_the_badge()
    {
        await using var server = await WeirServer.StartNewAsync();
        await using (var database = await server.StopForDatabaseAsync())
        {
            SeedSql.Execute(database.Connection, "DELETE FROM files");

            // What a pass records after probing: record_measured_media_facts lower-cases and joins codecs.
            InsertFile(
                database.Connection,
                FirstLibraryId(database.Connection),
                "Heat/heat.mkv",
                ("video_width", 3840),
                ("video_height", 2160),
                ("video_codec", "hevc"),
                ("audio_codecs", "truehd,ac3"),
                ("video_bit_depth", 10));
        }

        using var client = await server.CreateAdminClientAsync();
        var listed = await client.GetAsync(Devices);
        listed.ShouldBe(HttpStatusCode.OK);
        Assert.DoesNotContain(listed.Fields["devices"]!.AsArray(), device => (bool)device!["selected"]!);
        var before = await client.GetAsync(Files);
        Assert.Empty(before.Fields["files"]![0]!["direct_play"]!.AsArray());

        var saved = await client.PutWithCsrfAsync(
            Devices,
            Obj(("selected", Strings(["apple_tv_4k", "lg_webos", "no_such_device"]))));
        saved.ShouldBe(HttpStatusCode.OK);
        Assert.Equal(
            ["apple_tv_4k", "lg_webos"],
            saved.Fields["devices"]!.AsArray().Where(device => (bool)device!["selected"]!).Select(device => (string)device!["id"]!));

        var after = await client.GetAsync(Files);
        var badge = after.Fields["files"]![0]!["direct_play"]!.AsArray().ToDictionary(device => (string)device!["device_id"]!, device => device!);
        Assert.Equal("no", (string)badge["apple_tv_4k"]["verdict"]!);
        Assert.Contains("cannot play MKV files", badge["apple_tv_4k"]["reasons"]!.AsArray().Select(reason => (string)reason!));
        Assert.Equal("maybe", (string)badge["lg_webos"]["verdict"]!);

        var cleared = await client.PutWithCsrfAsync(Devices, Obj(("selected", new System.Text.Json.Nodes.JsonArray())));
        cleared.ShouldBe(HttpStatusCode.OK);
        Assert.DoesNotContain(cleared.Fields["devices"]!.AsArray(), device => (bool)device!["selected"]!);
    }
}
