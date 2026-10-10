using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Weir.Contract.Tests.Harness;

namespace Weir.E2E.Tests.Support;

/// <summary>
/// Changes Weir's data through its API as the signed-in person in a browser context, so the change is made by the server
/// as it would be by anyone else, never by writing to its database. The requests come from the test, not from the page,
/// so they get through while the page's own network is switched off.
/// </summary>
public static class WeirApi
{
    /// <summary>Whether the server says Processing is paused, asked as the signed-in person in the context.</summary>
    public static async Task<bool> IsPausedAsync(IBrowserContext context, string baseUrl)
    {
        await using var response = await context.APIRequest.GetAsync($"{baseUrl}/api/v1/pause");
        if (!response.Ok)
        {
            throw new InvalidOperationException($"Reading the pause answered {response.Status}: {await response.TextAsync()}");
        }

        return JsonDocument.Parse(await response.TextAsync()).RootElement.GetProperty("paused").GetBoolean();
    }

    public static async Task SetPausedAsync(IBrowserContext context, string baseUrl, bool paused)
    {
        var token = await CsrfTokenAsync(context, baseUrl);
        await using var response = await context.APIRequest.PutAsync($"{baseUrl}/api/v1/pause", new() { DataObject = new { csrf_token = token, paused } });
        await EnsureOkAsync(response, $"Setting the pause to {paused}");
    }

    /// <summary>Pauses Processing for `minutes`, after which the server resumes it by itself.</summary>
    public static async Task SetPausedForAsync(IBrowserContext context, string baseUrl, int minutes)
    {
        var token = await CsrfTokenAsync(context, baseUrl);
        await using var response = await context.APIRequest.PutAsync(
            $"{baseUrl}/api/v1/pause",
            new() { DataObject = new { csrf_token = token, paused = true, pause_for_minutes = minutes } });
        await EnsureOkAsync(response, $"Pausing for {minutes} minutes");
    }

    /// <summary>
    /// Keeps a failed file without processing it again, as the remove dialog's "Keep" does: the dialog's own request, confirmed
    /// with the file's details when Weir recorded none of its own.
    /// </summary>
    public static async Task KeepFileAsync(IBrowserContext context, string baseUrl, long fileId)
    {
        await using var options = await context.APIRequest.GetAsync($"{baseUrl}/api/v1/processing/files/{fileId}/remove-options");
        await EnsureOkAsync(options, $"Reading what can be done with file {fileId}");
        var offered = JsonDocument.Parse(await options.TextAsync()).RootElement;
        var token = await CsrfTokenAsync(context, baseUrl);
        await using var response = await context.APIRequest.DeleteAsync(
            $"{baseUrl}/api/v1/processing/files/{fileId}",
            new()
            {
                DataObject = new
                {
                    csrf_token = token,
                    resolution = "keep",
                    confirm_size_bytes = offered.GetProperty("unconfirmed_size_bytes").GetInt64(),
                    confirm_modified_at = offered.GetProperty("unconfirmed_modified_at").GetString(),
                },
            });
        await EnsureOkAsync(response, $"Keeping file {fileId}");
    }

    /// <summary>Points the first movie workflow at two folders, as the person does in Workflows.</summary>
    public static async Task SetMovieFoldersAsync(IBrowserContext context, string baseUrl, string watchedFolder, string outputFolder)
    {
        await using var list = await context.APIRequest.GetAsync($"{baseUrl}/api/v1/processing/libraries");
        await EnsureOkAsync(list, "Listing the workflows");
        var movie = JsonNode.Parse(await list.TextAsync())!.AsArray()
            .Select(library => library!.AsObject())
            .Where(library => (string)library["media_type"]! == "movie")
            .OrderBy(library => (int)library["display_order"]!)
            .First();

        var body = LibraryBodies.Unchanged(movie);
        body["watched_folder"] = watchedFolder;
        body["output_folder"] = outputFolder;
        body["csrf_token"] = await CsrfTokenAsync(context, baseUrl);
        await using var response = await context.APIRequest.PutAsync(
            $"{baseUrl}/api/v1/processing/libraries/{(int)movie["id"]!}",
            new() { DataObject = body });
        await EnsureOkAsync(response, "Saving the movie workflow's folders");
    }

    /// <summary>Asks Weir to work on one file of the movie workflow's watched folder, which queues a job for it.</summary>
    public static async Task EnqueueFileAsync(IBrowserContext context, string baseUrl, string relativeMediaPath)
    {
        var token = await CsrfTokenAsync(context, baseUrl);
        await using var response = await context.APIRequest.PostAsync(
            $"{baseUrl}/api/v1/processing/jobs/file-remux-pass/enqueue",
            new() { DataObject = new { csrf_token = token, relative_media_path = relativeMediaPath } });
        await EnsureOkAsync(response, $"Queueing {relativeMediaPath}");
    }

    /// <summary>"Restart and apply": asks the tray to install the update that was downloaded.</summary>
    public static async Task ApplyUpdateAsync(IBrowserContext context, string baseUrl)
    {
        var token = await CsrfTokenAsync(context, baseUrl);
        await using var response = await context.APIRequest.PostAsync($"{baseUrl}/api/v1/suite/apply-update", new() { DataObject = new { csrf_token = token } });
        await EnsureOkAsync(response, "Applying the update");
    }

    private static async Task<string> CsrfTokenAsync(IBrowserContext context, string baseUrl)
    {
        await using var csrf = await context.APIRequest.GetAsync($"{baseUrl}/api/v1/auth/csrf");
        return JsonDocument.Parse(await csrf.TextAsync()).RootElement.GetProperty("csrf_token").GetString()!;
    }

    private static async Task EnsureOkAsync(IAPIResponse response, string what)
    {
        if (!response.Ok)
        {
            throw new InvalidOperationException($"{what} answered {response.Status}: {await response.TextAsync()}");
        }
    }
}
