using Weir.Core.Configuration;

namespace Weir.Core.Tests.Configuration;

public sealed class ValueParsingAndListenTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("-12", -12L)]
    [InlineData("+3", 3L)]
    [InlineData("1_000_000", 1_000_000L)]
    [InlineData("99999999999999999999999", long.MaxValue)]
    [InlineData("-99999999999999999999999", long.MinValue)]
    public void Parses_signed_integers_with_underscores_and_clamps_overflow(string raw, long expected)
    {
        Assert.True(PythonCompat.TryParseInt(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("_1")]
    [InlineData("1_")]
    [InlineData("1__2")]
    [InlineData("1.0")]
    [InlineData("0x10")]
    [InlineData("1e3")]
    public void Rejects_malformed_integers(string raw) => Assert.False(PythonCompat.TryParseInt(raw, out _));

    [Theory]
    [InlineData("http://LocalHost:8782/path?q#f", "http", "localhost", 8782)]
    [InlineData("HTTPS://user:pw@127.0.0.1", "https", "127.0.0.1", null)]
    [InlineData("http://[::1]:9000", "http", "::1", 9000)]
    [InlineData("localhost:8782", "localhost", null, null)]
    [InlineData("*", "", null, null)]
    public void Splits_urls_into_their_parts(string raw, string scheme, string? hostname, int? port)
    {
        var parsed = PythonCompat.ParseUrl(raw);
        Assert.Equal(scheme, parsed.Scheme);
        Assert.Equal(hostname, parsed.Hostname);
        Assert.Equal(port, parsed.Port);
    }

    [Fact]
    public void Lexical_normalization_collapses_separators_and_dot_segments()
    {
        var posix = TestRuntime.Bare(isWindows: false);
        Assert.Equal("/srv/media/movies", PythonCompat.NormalizeLexically("/srv//media/./movies/", posix));
        Assert.Equal("//server/share", PythonCompat.NormalizeLexically("//server/share", posix));
        Assert.Equal("relative/dir", PythonCompat.NormalizeLexically("relative/./dir/", posix));
        Assert.Equal(".", PythonCompat.NormalizeLexically("./", posix));

        var windows = TestRuntime.Bare(isWindows: true);
        Assert.Equal(@"D:\media\movies", PythonCompat.NormalizeLexically("D:/media//movies/", windows));
        Assert.Equal(@"\\nas\share\tv", PythonCompat.NormalizeLexically(@"\\nas\share\tv\", windows));
    }

    [Fact]
    public void Tilde_expands_only_as_a_leading_segment()
    {
        var runtime = TestRuntime.Bare(isWindows: false);
        Assert.Equal(runtime.UserHomeDirectory, PythonCompat.ExpandUser("~", runtime));
        Assert.Equal(Path.Join(runtime.UserHomeDirectory, "x"), PythonCompat.ExpandUser("~/x", runtime));
        Assert.Equal("a/~/x", PythonCompat.ExpandUser("a/~/x", runtime));
        Assert.Equal("~other/x", PythonCompat.ExpandUser("~other/x", runtime));
    }

    [Fact]
    public void Listen_defaults_to_every_interface_on_9347()
    {
        Assert.Equal(new ServerListenOptions("0.0.0.0", 9347), ServerListenOptions.Parse([], TestRuntime.With()));
    }

    [Fact]
    public void Listen_prefers_the_tray_argument_over_the_docker_variable()
    {
        Assert.Equal(9000, ServerListenOptions.Parse([], TestRuntime.With(("PORT", "9000"))).Port);
        Assert.Equal(9100, ServerListenOptions.Parse(["--serve", "--port", "9100"], TestRuntime.With(("PORT", "9000"))).Port);
        Assert.Equal(new ServerListenOptions("127.0.0.1", 9347), ServerListenOptions.Parse(["--host", "127.0.0.1"], TestRuntime.With(("PORT", " "))));
    }

    [Fact]
    public void Listen_refuses_missing_invalid_and_out_of_range_values()
    {
        Assert.Equal("Missing value for --port.", Assert.Throws<WeirConfigurationException>(() => ServerListenOptions.Parse(["--port"], TestRuntime.With())).Message);
        Assert.Equal(
            "Invalid port abc: expected a number from 1 to 65535.",
            Assert.Throws<WeirConfigurationException>(() => ServerListenOptions.Parse(["--port", "abc"], TestRuntime.With())).Message);
        Assert.Throws<WeirConfigurationException>(() => ServerListenOptions.Parse([], TestRuntime.With(("PORT", "70000"))));
        Assert.Throws<WeirConfigurationException>(() => ServerListenOptions.Parse([], TestRuntime.With(("PORT", "0"))));
    }
}
