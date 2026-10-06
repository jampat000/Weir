namespace Weir.Contract.Tests.MediaManagers;

/// <summary>The server settings the media manager tests start servers with.</summary>
internal static class ManagerEnvironment
{
    public const string WebhookSecretVariable = "WEIR_MEDIA_MANAGER_WEBHOOK_SECRET";

    /// <summary>No instance-wide webhook secret, whatever the developer's shell holds.</summary>
    public static IReadOnlyDictionary<string, string> NoWebhookSecret { get; } =
        new Dictionary<string, string> { [WebhookSecretVariable] = string.Empty };

    public static IReadOnlyDictionary<string, string> WebhookSecret(string secret) =>
        new Dictionary<string, string> { [WebhookSecretVariable] = secret };
}
