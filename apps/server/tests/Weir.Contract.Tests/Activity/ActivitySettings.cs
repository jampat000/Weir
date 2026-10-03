using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Activity;

/// <summary>The suite settings the activity tests change: how long activity and logs are kept.</summary>
internal static class ActivitySettings
{
    public const int DefaultRetentionDays = 90;

    private const string SettingsPath = $"{WeirClient.Api}/suite/settings";

    public static async Task<JsonObject> CurrentAsync(WeirClient client)
    {
        var response = await client.GetAsync(SettingsPath);
        Assert.True(response.Status == HttpStatusCode.OK, response.ToString());
        return response.Fields;
    }

    /// <summary>A settings update that keeps every current value except the retention periods given.</summary>
    public static JsonObject UpdateBody(JsonObject current, int? logRetentionDays = null, int? activityRetentionDays = null) => new()
    {
        ["app_timezone"] = current["app_timezone"]?.DeepClone(),
        ["log_retention_days"] = logRetentionDays ?? (int)current["log_retention_days"]!,
        ["activity_retention_days"] = activityRetentionDays ?? (int)current["activity_retention_days"]!,
    };

    public static Task<WeirResponse> SaveAsync(WeirClient client, JsonObject body) => client.PutWithCsrfAsync(SettingsPath, body);

    public static async Task SetActivityRetentionAsync(WeirClient client, int days)
    {
        var saved = await SaveAsync(client, UpdateBody(await CurrentAsync(client), activityRetentionDays: days));
        Assert.True(saved.Status == HttpStatusCode.OK, saved.ToString());
    }
}
