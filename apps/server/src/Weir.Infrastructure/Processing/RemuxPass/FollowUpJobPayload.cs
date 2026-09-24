using Weir.Core.Json;

namespace Weir.Infrastructure.Processing.RemuxPass;

/// <summary>Shared payload parsing for the two failure-policy follow-up jobs (pass-through and reject).</summary>
internal static class FollowUpJobPayload
{
    /// <summary><c>json.loads(ctx.payload_json or "{}")</c>, tolerant of malformed or non-object JSON.</summary>
    public static WireObject Parse(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson))
        {
            return new WireObject();
        }

        try
        {
            return WireJsonParser.Parse(payloadJson) is WireObject dict ? dict : new WireObject();
        }
        catch (WireJsonDecodeException)
        {
            return new WireObject();
        }
    }

    public static string RelativeMediaPath(WireObject payload) =>
        WireStrings.Strip(payload.Get("relative_media_path") is WireString text ? text.Value : string.Empty);

    public static long? LibraryId(WireObject payload) =>
        payload.Get("library_id") is WireInteger number ? (long)number.Value : null;

    public static WireObject? Origin(WireObject payload) => payload.Get("origin") as WireObject;
}
