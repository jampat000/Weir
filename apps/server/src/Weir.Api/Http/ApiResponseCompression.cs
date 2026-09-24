using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace Weir.Api.Http;

/// <summary>
/// Compresses JSON and CSV responses (#712). List responses run to about a kilobyte a row and shrink five to ten times;
/// they are built per request, so the fastest level is used.
/// </summary>
/// <remarks>
/// <para>
/// Nothing under <see cref="UncompressedPrefix"/> is compressed. Those responses carry the CSRF token, and when an
/// attacker can both add text to a compressed response and see its length, the length gives the secret away a
/// character at a time (BREACH).
/// </para>
/// <para>
/// The live Activity stream (<c>text/event-stream</c>) is not in <see cref="MimeTypes"/>: a compressor holds frames
/// back until its buffer fills. Web assets are compressed once at build time and served by
/// <see cref="Web.CompressedStaticAssetsMiddleware"/>.
/// </para>
/// </remarks>
public static class ApiResponseCompression
{
    private static readonly PathString UncompressedPrefix = "/api/v1/auth";

    private static readonly string[] MimeTypes = ["application/json", "text/csv"];

    public static IServiceCollection AddWeirResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            // Behind a TLS proxy a request reads as HTTPS (TrustedProxySchemeMiddleware), and would otherwise never be
            // compressed. The BREACH exposure is handled by leaving UncompressedPrefix out.
            options.EnableForHttps = true;
            options.MimeTypes = MimeTypes;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        return services;
    }

    public static IApplicationBuilder UseWeirResponseCompression(this IApplicationBuilder app) =>
        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments(UncompressedPrefix, StringComparison.OrdinalIgnoreCase),
            branch => branch.UseResponseCompression());
}
