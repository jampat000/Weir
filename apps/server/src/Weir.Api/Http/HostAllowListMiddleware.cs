using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Weir.Core.Auth;
using Weir.Core.Configuration;
using Weir.Core.Net;

namespace Weir.Api.Http;

/// <summary>
/// Refuses a request whose Host header names an address Weir has no reason to trust (see
/// <see cref="AllowedHostPolicy"/>), before anything else in the pipeline answers it. Runs first so a
/// rejected request never reaches the static asset middleware, CORS, or routing.
/// </summary>
/// <remarks>
/// A request from a trusted reverse proxy (<c>WEIR_TRUSTED_PROXY_IPS</c>) skips this check: the proxy
/// is the one deciding which domains reach Weir, and it may forward more than one.
/// </remarks>
public sealed class HostAllowListMiddleware
{
    /// <summary>At most one warning per unrecognised host in this window, so a scanner cannot flood the log.</summary>
    private const int WarnWindowSeconds = 60;

    private readonly RequestDelegate _next;
    private readonly WeirOptions _options;
    private readonly IReadOnlyList<NetRange> _trustedProxies;
    private readonly ILogger _logger;
    private readonly SlidingWindowLimiter _warnLimiter;

    public HostAllowListMiddleware(RequestDelegate next, WeirOptions options, ILoggerFactory loggerFactory, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _next = next;
        _options = options;
        _trustedProxies = [.. options.TrustedProxyIps
            .Select(value => NetRange.TryParse(value.Trim(), strict: false, out var network) ? network : null)
            .OfType<NetRange>()];
        _logger = loggerFactory.CreateLogger("weir.platform.http.allowed_hosts");
        _warnLimiter = new SlidingWindowLimiter(maxEvents: 1, windowSeconds: WarnWindowSeconds, time);
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (FromTrustedProxy(context) || AllowedHostPolicy.IsAllowed(context.Request.Headers.Host.ToString(), _options.AllowedHosts, _options.TrustedBrowserOrigins))
        {
            return _next(context);
        }

        var host = context.Request.Headers.Host.ToString();
        if (_warnLimiter.Allow(host))
        {
            _logger.LogWarning("Refused a request with Host '{Host}', which is not in the allow-list.", host);
        }

        return ApiResponses.WritePlainTextAsync(
            context,
            StatusCodes.Status400BadRequest,
            string.Format(
                CultureInfo.InvariantCulture,
                "Weir doesn't recognise the address '{0}'. If this is how you reach Weir, add it to WEIR_ALLOWED_HOSTS.",
                host));
    }

    private bool FromTrustedProxy(HttpContext context)
    {
        var peer = context.Items.TryGetValue(ResponseMarkers.ConnectionPeer, out var item) ? item as string : ApiRequest.DefaultClientHost(context);
        return !string.IsNullOrEmpty(peer) && ClientRateLimitKey.InNetworks(peer, _trustedProxies);
    }
}
