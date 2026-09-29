using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Http;
using Weir.Core.Json;
using Weir.Infrastructure.Runtime;

namespace Weir.Api.Endpoints;

/// <summary>
/// Whether another device on the network can reach Weir, for System › About (docs/security-hardening.md#windows-firewall).
/// </summary>
public static class SuiteNetworkAccessEndpoints
{
    public static IEndpointRouteBuilder MapSuiteNetworkAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handlers = endpoints.ServiceProvider.GetRequiredService<SuiteNetworkAccessEndpointHandlers>();
        endpoints.MapV1("GET", "/suite/network-access", handlers.GetNetworkAccessAsync);
        return endpoints;
    }
}

/// <summary>Handler for <see cref="SuiteNetworkAccessEndpoints"/>.</summary>
internal sealed class SuiteNetworkAccessEndpointHandlers
{
    private readonly INetworkAccessReader _reader;

    public SuiteNetworkAccessEndpointHandlers(INetworkAccessReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<ApiResult> GetNetworkAccessAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(NetworkAccessStatusWire.From(_reader.ReadState()));
    }
}

/// <summary>The plain-language state and summary System › About shows, one line each, no jargon.</summary>
internal static class NetworkAccessStatusWire
{
    internal static WireObject From(NetworkAccessState state) => new WireObject()
        .Set("state", WireState(state))
        .Set("summary", Summary(state));

    private static string WireState(NetworkAccessState state) => state switch
    {
        NetworkAccessState.ThisPcOnly => "this_pc_only",
        NetworkAccessState.Allowed => "allowed",
        NetworkAccessState.Blocked => "blocked",
        _ => "not_applicable",
    };

    private static string Summary(NetworkAccessState state) => state switch
    {
        NetworkAccessState.ThisPcOnly =>
            "Only this PC can reach Weir. To let other devices on your network in, use the Weir tray icon → Allow other devices on your network.",
        NetworkAccessState.Allowed =>
            "Other devices on your network can reach Weir. To limit Weir to this PC, use the Weir tray icon → Only allow this PC.",
        NetworkAccessState.Blocked =>
            "Windows Firewall is blocking other devices. Use the Weir tray icon → Allow other devices on your network to fix it, or Only allow this PC.",
        _ => "",
    };
}
