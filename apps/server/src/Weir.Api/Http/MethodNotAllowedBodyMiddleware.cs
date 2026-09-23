using Microsoft.AspNetCore.Http;

namespace Weir.Api.Http;

/// <summary>
/// Routing answers a known path with the wrong method with an empty 405; existing clients and the contract
/// suite expect <c>{"detail": "Method Not Allowed"}</c> and an <c>Allow</c> header. This fills them in.
/// </summary>
public sealed class MethodNotAllowedBodyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RouteTable _routes;

    public MethodNotAllowedBodyMiddleware(RequestDelegate next, RouteTable routes)
    {
        _next = next;
        _routes = routes;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _next(context).ConfigureAwait(false);
        if (context.Response.StatusCode == StatusCodes.Status405MethodNotAllowed &&
            !context.Response.HasStarted &&
            context.Response.ContentLength is null or 0 &&
            string.IsNullOrEmpty(context.Response.ContentType))
        {
            // Allow names the methods of the first route whose path matched, not the union of all of them.
            if (_routes.FirstPathMatch(context.Request.Path) is { } match)
            {
                context.Response.Headers.Allow = string.Join(", ", match.Methods);
            }

            await ApiJson.WriteDetailAsync(context, StatusCodes.Status405MethodNotAllowed, "Method Not Allowed").ConfigureAwait(false);
        }
    }
}
