using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Weir.LiveAudit;

/// <summary>The machine-readable summary written to <c>summary.json</c> and used for the closing line.</summary>
internal sealed record AuditReport(
    [property: JsonPropertyName("base_url")] string BaseUrl,
    [property: JsonPropertyName("server_version")] string ServerVersion,
    [property: JsonPropertyName("steps")] IReadOnlyList<string> Steps,
    [property: JsonPropertyName("http_checks")] IReadOnlyList<string> HttpChecks,
    [property: JsonPropertyName("screenshots")] IReadOnlyList<string> Screenshots,
    [property: JsonPropertyName("console_warnings")] IReadOnlyList<string> ConsoleWarnings,
    [property: JsonPropertyName("console_errors")] IReadOnlyList<string> ConsoleErrors,
    [property: JsonPropertyName("page_errors")] IReadOnlyList<string> PageErrors,
    [property: JsonPropertyName("failed_requests")] IReadOnlyList<string> FailedRequests,
    [property: JsonPropertyName("bad_responses")] IReadOnlyList<string> BadResponses)
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
