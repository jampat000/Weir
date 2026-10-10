using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>
/// The tray has the running server save a copy of Weir's data before it applies an update, through two files in the data
/// folder: <c>update-backup-request.json</c> and the server's answer in <c>update-backup-result.json</c>.
/// </summary>
[ContractArea("system")]
public sealed class UpdateBackupRequestTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private WeirServer Server => fixture.Server;

    private string RequestPath => Path.Combine(Server.Home, "update-backup-request.json");

    private string ResultPath => Path.Combine(Server.Home, "update-backup-result.json");

    private async Task<JsonNode> AskAsync(string id, string target)
    {
        await File.WriteAllTextAsync(
            RequestPath,
            $"{{\"id\": \"{id}\", \"requested_at\": \"{DateTimeOffset.UtcNow:O}\", \"target_version\": \"{target}\"}}");
        return await Poll.UntilAsync(
            () => Task.FromResult(ReadResult(id)),
            "the server to answer the request for a copy of its data");
    }

    private JsonNode? ReadResult(string id)
    {
        try
        {
            using var stream = new FileStream(ResultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var answer = JsonNode.Parse(stream);
            return (string?)answer?["id"] == id && (string?)answer?["state"] is "saved" or "failed" ? answer : null;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    [Fact]
    public async Task A_running_server_says_it_can_take_the_request()
    {
        var ready = await Poll.UntilAsync(
            () => Task.FromResult(File.Exists(Path.Combine(Server.Home, "update-backup-ready"))
                ? JsonNode.Parse(File.ReadAllText(Path.Combine(Server.Home, "update-backup-ready")))
                : null),
            "the server to say it can take a request for a copy of its data");

        Assert.True((int)ready["pid"]! > 0);
        Assert.EndsWith("Z", (string)ready["started_at"]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_running_server_saves_a_consistent_copy_of_its_data_when_asked()
    {
        var answer = await AskAsync("contract-saved", "9.9.9");

        Assert.Equal("saved", (string?)answer["state"]);
        var path = (string)answer["path"]!;
        Assert.Equal(Path.Combine(Server.Home, "backups", "pre-update"), Path.GetDirectoryName(path));
        Assert.Matches(@"^weir-\d{4}-to-9\.9\.9-\d{8}T\d{6}Z\.db$", Path.GetFileName(path));
        Assert.True(new FileInfo(path).Length > 0);
        await Poll.UntilAsync(() => Task.FromResult(!File.Exists(RequestPath)), "the server to take the answered request away");

        using var admin = await Server.CreateAdminClientAsync();
        var overview = (await admin.GetAsync(WeirClient.Api + "/system/overview")).Fields;
        Assert.Equal(path, (string?)overview["last_update_backup"]!["path"]);
        Assert.Equal("9.9.9", (string?)overview["last_update_backup"]!["to_version"]);
        Assert.Equal((string?)overview["version"], (string?)overview["last_update_backup"]!["from_version"]);
    }
}
