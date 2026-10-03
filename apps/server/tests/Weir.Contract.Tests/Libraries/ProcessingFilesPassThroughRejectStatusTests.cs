using System.Net;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;

namespace Weir.Contract.Tests.Libraries;

/// <summary>
/// #530: GET /api/v1/processing/files must not 500 on a page containing a <c>passed_through</c> or
/// <c>rejected</c> row (both file statuses since #471), and filtering by either status must work.
/// </summary>
[ContractArea("libraries")]
public sealed class ProcessingFilesPassThroughRejectStatusTests
{
    private const string Files = Api + "/processing/files";

    [Fact]
    public async Task Files_lists_and_filters_passed_through_and_rejected_rows()
    {
        await using var server = await WeirServer.StartNewAsync();
        await using (var database = await server.StopForDatabaseAsync())
        {
            var libraryId = FirstLibraryId(database.Connection);
            InsertFile(
                database.Connection,
                libraryId,
                "PassThrough/one.mkv",
                ("status", "passed_through"),
                ("status_reason", "Weir could not process this file, so it handed the original back unchanged."));
            InsertFile(
                database.Connection,
                libraryId,
                "Rejected/one.mkv",
                ("status", "rejected"),
                ("status_reason", "Weir rejected this release: no retainable audio."));
        }

        // The client comes after the seed block: the server restarts on a new port when the block ends.
        using var admin = await server.CreateAdminClientAsync();

        var response = await admin.GetAsync(Files);
        response.ShouldBe(HttpStatusCode.OK);
        var statuses = response.Fields["files"]!.AsArray()
            .ToDictionary(row => (string)row!["relative_path"]!, row => (string)row!["status"]!);
        Assert.Equal("passed_through", statuses["PassThrough/one.mkv"]);
        Assert.Equal("rejected", statuses["Rejected/one.mkv"]);

        var byPassThrough = await admin.GetAsync(Files, ("file_status", "passed_through"));
        byPassThrough.ShouldBe(HttpStatusCode.OK);
        Assert.Equal(
            ["PassThrough/one.mkv"],
            byPassThrough.Fields["files"]!.AsArray().Select(row => (string)row!["relative_path"]!));

        var byRejected = await admin.GetAsync(Files, ("file_status", "rejected"));
        byRejected.ShouldBe(HttpStatusCode.OK);
        Assert.Equal(
            ["Rejected/one.mkv"],
            byRejected.Fields["files"]!.AsArray().Select(row => (string)row!["relative_path"]!));
    }
}
