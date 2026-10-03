using System.Net;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.Auth;

/// <summary>
/// Sign-in and bootstrap rate limiting, as a client sees it. Login and bootstrap each have a per-address sliding window
/// configured by <c>WEIR_AUTH_LOGIN_RATE_*</c> and <c>WEIR_BOOTSTRAP_RATE_*</c>; exceeding it answers 429 with <c>Retry-After</c>.
/// </summary>
[ContractArea("auth")]
public sealed class RateLimitTests(RateLimitTests.RateLimitServerFixture fixture) : AuthTestBase(fixture), IClassFixture<RateLimitTests.RateLimitServerFixture>
{
    // Long enough that three Argon2-hashed sign-ins fit inside it on a slow CI runner.
    private const int WindowSeconds = 10;

    [Fact]
    public async Task Login_rate_limit_window_slides()
    {
        var session = NewSession();
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.LoginAsync(password: "wrong")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.LoginAsync(password: "wrong")).Status);
        var limited = await session.LoginAsync(password: "wrong");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal(WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), limited.Header("Retry-After"));

        await Poll.UntilAsync(
            async () => (await session.LoginAsync(password: "wrong")).Status == HttpStatusCode.Unauthorized,
            "the login window to slide past the earlier attempts",
            TimeSpan.FromSeconds(WindowSeconds * 5));
    }

    [Fact]
    public async Task Bootstrap_rate_limited()
    {
        var session = NewSession();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await session.BootstrapAsync("owner1", "password1234")).Status);
        }

        var limited = await session.BootstrapAsync("owner1", "password1234");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.Equal("3600", limited.Header("Retry-After"));
    }

    public sealed class RateLimitServerFixture : ServerFixture
    {
        protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
        {
            ["WEIR_AUTH_LOGIN_RATE_MAX_ATTEMPTS"] = "2",
            ["WEIR_AUTH_LOGIN_RATE_WINDOW_SECONDS"] = WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["WEIR_BOOTSTRAP_RATE_MAX_ATTEMPTS"] = "2",
            ["WEIR_BOOTSTRAP_RATE_WINDOW_SECONDS"] = "3600",
        };
    }
}
