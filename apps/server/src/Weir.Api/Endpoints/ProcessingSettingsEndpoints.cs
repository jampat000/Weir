using Microsoft.AspNetCore.Routing;

namespace Weir.Api.Endpoints;

/// <summary>
/// Operator-editable automation settings, the read-only runtime snapshot, the hardware-acceleration
/// report and the metadata-provider connection (the settings half only; see <see cref="Weir.Infrastructure.Processing.MetadataProviderStore"/>).
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
