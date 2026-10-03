using System.Net;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>Directory browsing for folder pickers (GET <c>/api/v1/system/directories</c>).</summary>
[ContractArea("system")]
public sealed class LocalBrowseApiTests(UsersFixture fixture) : IClassFixture<UsersFixture>
{
    private const string Directories = $"{WeirClient.Api}/system/directories";

    [Fact]
    public async Task System_directories_requires_operator()
    {
        using var viewer = await SeededAccounts.SignInViewerAsync(fixture.Server);

        var response = await viewer.GetAsync(Directories);

        Assert.Equal(HttpStatusCode.Forbidden, response.Status);
    }

    [Fact]
    public async Task System_directories_lists_roots()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.GetAsync(Directories);

        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        var body = response.Fields;
        Assert.True(body.ContainsKey("current_path"));
        Assert.Null(body["current_path"]);
        Assert.True(body.ContainsKey("parent_path"));
        Assert.Null(body["parent_path"]);
        var entries = body["entries"]!.AsArray();
        Assert.NotEmpty(entries);
        var first = entries[0]!.AsObject();
        foreach (var key in new[] { "name", "path", "kind", "description" })
        {
            Assert.True(first.ContainsKey(key), key);
        }
    }

    [Fact]
    public async Task System_directories_returns_not_found()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();
        // The server runs on this machine, so a folder missing under this temp directory is missing for it too.
        var missingPath = Path.Combine(Path.GetTempPath(), $"weir-missing-{Guid.NewGuid()}");

        var response = await admin.GetAsync(Directories, ("path", missingPath));

        Assert.Equal(HttpStatusCode.NotFound, response.Status);
        Assert.Equal("The requested directory does not exist.", (string?)response.Fields["detail"]);
    }

    [Theory]
    [InlineData("\\\\attacker.example\\share")]
    [InlineData("//attacker.example/share")]
    [InlineData("\\\\?\\C:\\Windows")]
    [InlineData("\\\\.\\PhysicalDrive0")]
    public async Task System_directories_rejects_unc_and_device_paths(string path)
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var response = await admin.GetAsync(Directories, ("path", path));

        Assert.True(response.Status == HttpStatusCode.BadRequest, response.ToString());
        Assert.Equal("UNC and device paths are not allowed.", (string?)response.Fields["detail"]);
    }
}
