using System.Text.Json;
using Microsoft.Playwright;

namespace Weir.E2E.Tests.Support;

/// <summary>
/// Changes Weir's data through its API as the signed-in person in a browser context, so the change is made by the server
/// as it would be by anyone else, never by writing to its database. The requests come from the test, not from the page,
/// so they get through while the page's own network is switched off.
/// </summary>
public static class WeirApi
{
    public static async Task SetPausedAsync(IBrowserContext context, string baseUrl, bool paused)
    {
        await using var csrf = await context.APIRequest.GetAsync($"{baseUrl}/api/v1/auth/csrf");
        var token = JsonDocument.Parse(await csrf.TextAsync()).RootElement.GetProperty("csrf_token").GetString();
        await using var response = await context.APIRequest.PutAsync($"{baseUrl}/api/v1/pause", new() { DataObject = new { csrf_token = token, paused } });
        if (!response.Ok)
        {
            throw new InvalidOperationException($"Setting the pause to {paused} answered {response.Status}: {await response.TextAsync()}");
        }
    }
}
