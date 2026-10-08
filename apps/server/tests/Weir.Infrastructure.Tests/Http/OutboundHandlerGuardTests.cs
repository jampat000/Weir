using System.Text.RegularExpressions;

namespace Weir.Infrastructure.Tests.Http;

/// <summary>
/// Every outbound connection Weir makes goes through the one loopback-first connect rule, so no code path can
/// reintroduce the two-second <c>localhost</c> wait. The scan covers the server's production source; the tray only
/// calls Weir by its 127.0.0.1 address.
/// </summary>
public sealed partial class OutboundHandlerGuardTests
{
    /// <summary>Source files allowed a bare <c>new HttpClient</c>, each with why it never meets a host name.</summary>
    private static readonly Dictionary<string, string> BareClientExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        [Path.Join("Weir.Host", "HealthCheckCommand.cs")] = "the container healthcheck requests the 127.0.0.1 address literal",
    };

    [Fact]
    public void Server_source_builds_no_http_client_or_handler_that_skips_the_shared_connect_rule()
    {
        var violations = new List<string>();
        var handlers = 0;
        foreach (var file in ProductionSourceFiles())
        {
            var source = File.ReadAllText(file);
            var name = Path.GetRelativePath(ServerSourceDirectory(), file);
            if (BareHttpClient().IsMatch(source) && !BareClientExceptions.ContainsKey(name))
            {
                violations.Add($"{name}: new HttpClient() with no handler");
            }

            if (source.Contains("new HttpClientHandler", StringComparison.Ordinal))
            {
                violations.Add($"{name}: new HttpClientHandler");
            }

            foreach (Match match in SocketsHandler().Matches(source))
            {
                handlers++;
                var initializer = Initializer(source, match.Index + match.Length);
                if (!SharedConnect().IsMatch(initializer))
                {
                    violations.Add($"{name}: new SocketsHttpHandler without the shared ConnectCallback");
                }
            }
        }

        Assert.True(handlers >= 2, "The scan found no SocketsHttpHandler; it is looking in the wrong place.");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>The object initializer that follows a <c>new SocketsHttpHandler</c>, braces balanced; empty when there is none.</summary>
    private static string Initializer(string source, int from)
    {
        var start = from;
        while (start < source.Length && char.IsWhiteSpace(source[start]))
        {
            start++;
        }

        if (start >= source.Length || source[start] != '{')
        {
            return string.Empty;
        }

        var depth = 0;
        for (var index = start; index < source.Length; index++)
        {
            depth += source[index] == '{' ? 1 : source[index] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        return source[start..];
    }

    private static IEnumerable<string> ProductionSourceFiles() =>
        Directory.EnumerateFiles(ServerSourceDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string ServerSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var source = Path.Join(directory.FullName, "apps", "server", "src");
            if (Directory.Exists(source))
            {
                return source;
            }
        }

        throw new DirectoryNotFoundException("apps/server/src was not found above the test output directory.");
    }

    [GeneratedRegex(@"new\s+HttpClient\s*\(\s*\)|new\s+HttpClient\s*\{")]
    private static partial Regex BareHttpClient();

    [GeneratedRegex(@"new\s+SocketsHttpHandler\b")]
    private static partial Regex SocketsHandler();

    [GeneratedRegex(@"ConnectCallback[\s\S]*?(LoopbackFirstConnect|OutboundAddressGuard)\.ConnectAsync")]
    private static partial Regex SharedConnect();
}
