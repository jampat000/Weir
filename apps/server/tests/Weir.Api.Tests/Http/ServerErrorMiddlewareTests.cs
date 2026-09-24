using Microsoft.AspNetCore.Http;
using Weir.Api.Http;
using Weir.Core.Configuration;

namespace Weir.Api.Tests.Http;

/// <summary>
/// The outermost error handler answers 500 itself, so <see cref="SecurityHeadersMiddleware"/> never runs for
/// that response (see <see cref="ResponseMarkers.ServerError"/>); this drives <see cref="ServerErrorMiddleware"/>
/// directly to prove it applies the same baseline headers before writing the body.
/// </summary>
public sealed class ServerErrorMiddlewareTests
{
    [Fact]
    public async Task A_500_response_still_carries_nosniff_and_the_rest()
    {
        var home = Path.Join(Path.GetTempPath(), "weir-server-error-tests-" + Guid.NewGuid().ToString("N"));
        var options = WeirOptionsLoader.Load(new RuntimeEnvironment(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["WEIR_HOME"] = home },
            OperatingSystem.IsWindows(),
            home,
            home));
        var middleware = new ServerErrorMiddleware(_ => throw new InvalidOperationException("boom"), options);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Opener-Policy"]);
        Assert.Equal("same-origin", context.Response.Headers["Cross-Origin-Resource-Policy"]);
        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=(), usb=()", context.Response.Headers["Permissions-Policy"]);
    }
}
