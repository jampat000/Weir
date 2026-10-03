using System.IO.Compression;
using System.Net;
using System.Text;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Auth.AuthSupport;

namespace Weir.Contract.Tests.Auth;

/// <summary>
/// Trusted-proxy and compressed-asset handling, observed on a real server: the forwarded scheme through the sign-in
/// cookie's <c>Secure</c> flag, and the assets through a <c>WEIR_WEB_DIST</c> folder with pre-compressed siblings.
/// </summary>
[ContractArea("auth")]
public sealed class HttpHardeningTests(HttpHardeningTests.ProxyServerFixture fixture) : AuthTestBase(fixture), IClassFixture<HttpHardeningTests.ProxyServerFixture>
{
    [Fact]
    public async Task Trusted_proxy_applies_one_forwarded_scheme_only_for_trusted_peer()
    {
        var session = NewSession();
        await session.EnsureAdminAccountAsync();

        var https = await session.LoginAsync(headers: Headers(("X-Forwarded-Proto", "https")));
        AssertStatus(HttpStatusCode.OK, https);
        Assert.Contains("secure", SetCookieHeader(https).ToLowerInvariant(), StringComparison.Ordinal);

        // A chain is ambiguous (which hop is ours?), so the request stays plain HTTP.
        var chain = await session.LoginAsync(headers: Headers(("X-Forwarded-Proto", "https, http")));
        AssertStatus(HttpStatusCode.OK, chain);
        Assert.DoesNotContain("secure", SetCookieHeader(chain).ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compressed_assets_negotiate_and_cache()
    {
        var session = NewSession();

        // The .br sibling holds fake bytes a brotli decoder would reject, so read the body undecoded.
        var (br, brBody) = await session.GetRawAsync("/assets/app-abc.js", Headers(("Accept-Encoding", "br, gzip")));
        using (br)
        {
            Assert.Equal(HttpStatusCode.OK, br.StatusCode);
            Assert.Equal("brotli-bytes", Encoding.ASCII.GetString(brBody));
            Assert.Equal("br", AuthSession.RawHeader(br, "Content-Encoding"));
            Assert.Equal("public, max-age=31536000, immutable", AuthSession.RawHeader(br, "Cache-Control"));
            Assert.Equal("Accept-Encoding", AuthSession.RawHeader(br, "Vary"));
            var etag = AuthSession.RawHeader(br, "ETag");
            Assert.False(string.IsNullOrEmpty(etag));

            var (gzipResponse, gzipBody) = await session.GetRawAsync("/assets/app-abc.js", Headers(("Accept-Encoding", "gzip")));
            using (gzipResponse)
            {
                Assert.Equal(HttpStatusCode.OK, gzipResponse.StatusCode);
                Assert.Equal("gzip-bytes", Encoding.ASCII.GetString(Gunzip(gzipBody)));
                Assert.Equal("gzip", AuthSession.RawHeader(gzipResponse, "Content-Encoding"));
            }

            var (unchanged, unchangedBody) = await session.GetRawAsync("/assets/app-abc.js", Headers(("Accept-Encoding", "br"), ("If-None-Match", etag!)));
            using (unchanged)
            {
                Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
                Assert.Empty(unchangedBody);
            }
        }
    }

    [Fact]
    public async Task Compressed_assets_do_not_intercept_range_requests()
    {
        var (response, body) = await NewSession().GetRawAsync("/assets/range-abc.js", Headers(("Accept-Encoding", "gzip"), ("Range", "bytes=0-3")));
        using (response)
        {
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("0123", Encoding.ASCII.GetString(body));
        }
    }

    private static byte[] Gunzip(byte[] compressed)
    {
        using var input = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>A trusted proxy at the loopback address, and a web app folder with pre-compressed siblings.</summary>
    public sealed class ProxyServerFixture : ServerFixture, IDisposable
    {
        private readonly string _dist = WebDist.Write(
            WebDist.DefaultIndex,
            new Dictionary<string, byte[]>
            {
                ["app-abc.js"] = Encoding.UTF8.GetBytes("console.log('source');"),
                ["app-abc.js.br"] = Encoding.ASCII.GetBytes("brotli-bytes"),
                ["app-abc.js.gz"] = Gzip(Encoding.ASCII.GetBytes("gzip-bytes")),
                ["range-abc.js"] = Encoding.ASCII.GetBytes("0123456789"),
                ["range-abc.js.gz"] = Encoding.ASCII.GetBytes("compressed"),
            });

        protected override IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
        {
            ["WEIR_TRUSTED_PROXY_IPS"] = "127.0.0.1/32",
            ["WEIR_WEB_DIST"] = _dist,
        };

        public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dist)!, recursive: true);

        private static byte[] Gzip(byte[] plain)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                gzip.Write(plain);
            }

            return output.ToArray();
        }
    }
}
