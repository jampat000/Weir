using Microsoft.AspNetCore.Routing;

namespace Weir.Api.Endpoints;

/// <summary>
/// Operator-editable automation settings, the read-only runtime snapshot, the hardware-acceleration
/// report and the deprecated metadata-provider routes (see <see cref="ProcessingMetadataProviderEndpoints"/>).
/// </summary>
public static class ProcessingSettingsEndpoints
{
    public static IEndpointRouteBuilder MapProcessingSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapProcessingOperatorSettingsEndpoints();
        endpoints.MapProcessingRuntimeEndpoints();
        endpoints.MapProcessingMetadataProviderEndpoints();
        return endpoints;
    }
}
