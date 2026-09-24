using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weir.Infrastructure.Processes;

namespace Weir.Infrastructure.Media;

/// <summary>
/// The one registration of ffprobe/ffmpeg discovery, the process runner and <see cref="MediaTools"/>. Every area
/// that runs media tools calls it, so whichever area registers first, all of them get the same resolver: the one
/// that also looks beside a single-file publish, where the Windows package bundles ffmpeg.
/// </summary>
public static class MediaToolServices
{
    public static IServiceCollection AddWeirMediaTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMediaToolResolver>(_ => MediaToolResolver.ForCurrentProcess());
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<MediaTools>();
        return services;
    }
}
