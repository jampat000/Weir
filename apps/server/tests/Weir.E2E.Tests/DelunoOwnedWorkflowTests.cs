using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;
using Weir.E2E.Tests.Harness;
using Weir.E2E.Tests.Support;
using static Microsoft.Playwright.Assertions;

namespace Weir.E2E.Tests;

public sealed class DelunoOwnedWorkflowTests(E2EServer server) : E2ETestBase(server)
{
    private const string Libraries = $"{WeirClient.Api}/processing/libraries";
    private const string Connections = $"{WeirClient.Api}/media-managers/connections";
    private const string DestinationsPath = "/api/integrations/processors/download-destinations";

    [E2EFact]
    public async Task A_workflow_set_up_from_Deluno_shows_its_folders_as_Deluno_s_until_it_is_unlinked()
    {
        using var rig = new TemporaryFolder();
        var saveFolder = Directory.CreateDirectory(Path.Join(rig.Path, "Completed", "Movies")).FullName;
        var readyFolder = Directory.CreateDirectory(Path.Join(rig.Path, "Ready", "Movies")).FullName;
        using var deluno = FakeManager.StartDeluno([new JsonObject
        {
            ["id"] = "lib-movies",
            ["name"] = "Movies",
            ["mediaType"] = "movie",
            ["rootPath"] = "/library/lib-movies",
            ["importWorkflow"] = "refine-before-import",
            ["processorOutputPath"] = readyFolder,
            ["downloadsPath"] = string.Empty,
        }]);
        deluno.Route("GET", DestinationsPath, new JsonObject
        {
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["libraryId"] = "lib-movies",
                ["libraryName"] = "Movies",
                ["downloadsPath"] = string.Empty,
                ["processorOutputPath"] = readyFolder,
                ["destinations"] = new JsonArray(new JsonObject
                {
                    ["downloadClientName"] = "qBittorrent",
                    ["category"] = "weir",
                    ["categoryKind"] = "category",
                    ["saveFolder"] = saveFolder,
                    ["savedBy"] = "client-category",
                    ["status"] = "ok",
                    ["message"] = string.Empty,
                }),
                ["processorConnection"] = new JsonObject { ["pathMappings"] = new JsonArray() },
            }),
        });
        using var client = new WeirClient(new Uri(BaseUrl));
        await client.EnsureAdminAsync(Navigation.BootstrapUser, Navigation.BootstrapPassword);
        var created = await client.PostWithCsrfAsync(Connections, new JsonObject
        {
            ["kind"] = "deluno",
            ["base_url"] = deluno.BaseUrl,
            ["api_key"] = deluno.ApiKey,
            ["enabled"] = true,
        });
        Assert.True(created.Status == HttpStatusCode.Created, created.ToString());
        var delunoId = (long)created.Fields["id"]!;
        // A connection is named after its kind and the host in its address, and the editor says whose a folder is by that name.
        var name = (string)created.Fields["name"]!;
        var ownedNote = $"From {name}; change it in {name}.";
        try
        {
            var movies = await Poll.UntilAsync(
                async () =>
                {
                    var listed = await client.GetAsync(Libraries);
                    Assert.True(listed.Status == HttpStatusCode.OK, listed.ToString());
                    return listed.Elements
                        .Select(row => row!.AsObject())
                        .FirstOrDefault(row => (long?)row["folders_synced_from_connection_id"] == delunoId && (string)row["media_type"]! == "movie");
                },
                "the Movies workflow to be set up from Deluno");
            var moviesId = (long)movies["id"]!;

            var page = await NewPageAsync();

            await Navigation.EnsureSignedInAsync(page, BaseUrl);
            await Navigation.OpenTabAsync(page, "Workflows", "File paths");
            await page.GetByTestId($"processing-library-{moviesId}").GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }).ClickAsync();
            var form = page.GetByTestId("processing-library-form");
            var watched = form.GetByRole(AriaRole.Textbox, new() { Name = "Watched folder" });
            var output = form.GetByRole(AriaRole.Textbox, new() { Name = "Output folder" });
            var work = form.GetByRole(AriaRole.Textbox, new() { Name = "Work folder" });

            // Deluno owns the watched and output folders; the work folder stays the person's.
            await Expect(watched).ToHaveValueAsync(saveFolder);
            await Expect(output).ToHaveValueAsync(readyFolder);
            await Expect(watched).Not.ToBeEditableAsync();
            await Expect(output).Not.ToBeEditableAsync();
            await Expect(work).ToBeEditableAsync();
            await Expect(form.GetByText(ownedNote, new() { Exact = true })).ToHaveCountAsync(2);

            await form.GetByRole(AriaRole.Heading, new() { Name = "Media manager", Exact = true }).ClickAsync();
            await form.GetByTestId("library-unlink").ClickAsync();
            await Expect(watched).ToBeEditableAsync();
            await Expect(output).ToBeEditableAsync();
            await Expect(form.GetByText(ownedNote, new() { Exact = true })).ToHaveCountAsync(0);

            await page.GetByTestId("processing-library-save").ClickAsync();
            await Expect(form).ToHaveCountAsync(0);
            var saved = await client.GetAsync($"{Libraries}/{moviesId}");
            Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
            Assert.Null(saved.Fields["folders_synced_from_connection_id"]);
        }
        finally
        {
            var deleted = await client.DeleteWithCsrfBodyAsync($"{Connections}/{delunoId}");
            Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        }
    }
}
