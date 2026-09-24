using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Weir.Api.Http;
using Weir.Core.Auth;
using Weir.Core.Json;
using Weir.Core.Validation;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Settings;

namespace Weir.Api.Endpoints;

/// <summary>The metadata-provider connection settings: reading, updating and test-connecting (see <see cref="MetadataProviderStore"/>).</summary>
public static class ProcessingMetadataProviderEndpoints
{
    public static IEndpointRouteBuilder MapProcessingMetadataProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var handlers = endpoints.ServiceProvider.GetRequiredService<ProcessingMetadataProviderEndpointHandlers>();
        endpoints.MapV1("GET", "/processing/metadata-provider", handlers.GetMetadataProviderAsync);
        endpoints.MapV1("PUT", "/processing/metadata-provider", handlers.PutMetadataProviderAsync);
        endpoints.MapV1("POST", "/processing/metadata-provider/test", handlers.PostMetadataProviderTestAsync);
        return endpoints;
    }
}

/// <summary>Handlers for <see cref="ProcessingMetadataProviderEndpoints"/>, constructor-injected with the stores they need.</summary>
internal sealed class ProcessingMetadataProviderEndpointHandlers
{
    private readonly MetadataProviderStore _metadataProvider;
    private readonly MetadataProviderService _metadataProviderService;
    private readonly SuiteSettingsStore _suiteSettings;

    public ProcessingMetadataProviderEndpointHandlers(
        MetadataProviderStore metadataProvider, MetadataProviderService metadataProviderService, SuiteSettingsStore suiteSettings)
    {
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        _metadataProviderService = metadataProviderService ?? throw new ArgumentNullException(nameof(metadataProviderService));
        _suiteSettings = suiteSettings ?? throw new ArgumentNullException(nameof(suiteSettings));
    }

    private static WireObject MetadataProviderOut(MetadataProviderView view) => new WireObject()
        .Set("provider", view.Provider)
        .Set("base_url", view.BaseUrl)
        .Set("key_configured", view.KeyConfigured)
        .Set("known_providers", new WireArray(view.KnownProviders.Select(p => (WireValue)WireValue.Of(p))));

    public async Task<ApiResult> GetMetadataProviderAsync(ApiRequest request)
    {
        await request.RequireUserAsync().ConfigureAwait(false);
        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(MetadataProviderOut(_metadataProvider.View(row)));
    }

    public async Task<ApiResult> PutMetadataProviderAsync(ApiRequest request)
    {
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        var provider = model.Literal("provider", ["", "tmdb"], defaultValue: "");
        var baseUrl = model.OptionalStr("base_url", defaultValue: "", maxLength: 500) ?? string.Empty;
        var apiKey = model.OptionalStr("api_key", maxLength: 500);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await _metadataProvider.ApplyAsync(uow, request.Options, request.Time, provider, baseUrl, apiKey).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        return ApiRoutes.Ok(MetadataProviderOut(_metadataProvider.View(row)));
    }

    public async Task<ApiResult> PostMetadataProviderTestAsync(ApiRequest request)
    {
        // Same body shape as PUT (MetadataProviderIn); the test itself only reads what is already saved.
        var payload = await request.ReadBodyAsync().ConfigureAwait(false);
        var issues = new ValidationIssues();
        var model = new BodyModel(payload, issues);
        var csrfToken = model.Str("csrf_token", minLength: 1);
        model.Literal("provider", ["", "tmdb"], defaultValue: "");
        model.OptionalStr("base_url", defaultValue: "", maxLength: 500);
        model.OptionalStr("api_key", maxLength: 500);
        model.Finish(ExtraFields.Forbid);
        issues.ThrowIfAny();

        await request.RequireUserAsync(UserRoles.OperatorOrAdmin).ConfigureAwait(false);
        request.RequireConfirmationToken(csrfToken);

        var uow = await request.DbAsync().ConfigureAwait(false);
        var row = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        await request.CommitAsync().ConfigureAwait(false);
        var result = string.IsNullOrWhiteSpace(row.MetadataProviderKeyCiphertext) || string.IsNullOrWhiteSpace(row.MetadataProvider)
            ? _metadataProvider.Test(row)
            : await _metadataProviderService.TestProviderAsync(uow, request.Context.RequestAborted).ConfigureAwait(false);
        return ApiRoutes.Ok(new WireObject().Set("status", result.Status).Set("detail", result.Detail));
    }
}
