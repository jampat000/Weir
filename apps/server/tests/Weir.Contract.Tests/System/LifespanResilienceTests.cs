using System.Net;
using System.Text;
using System.Text.Json;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.SystemArea;

/// <summary>A failing non-essential startup step must not stop the server starting.</summary>
[ContractArea("system")]
public sealed class LifespanResilienceTests
{
    /// <summary>
    /// A log file the startup clean-up cannot read (not UTF-8) must not stop the server starting.
    /// The startup log-retention step fails on its own here, because the active log it rewrites holds
    /// bytes that are not text.
    /// </summary>
    [Fact]
    public async Task Non_essential_startup_failure_does_not_abort_startup()
    {
        await using var server = await WeirServer.StartNewAsync();
        await server.StopAsync();
        var logs = Path.Combine(server.Home, "logs");
        Directory.CreateDirectory(logs);
        await File.WriteAllBytesAsync(Path.Combine(logs, "weir.log"), NotText());

        await server.RestartAsync();

        using var client = server.CreateClient();
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.Status);
        Assert.Equal("ok", (string?)health.Fields["status"]);
        var ready = await client.GetAsync("/ready");
        Assert.True(ready.Status == HttpStatusCode.OK, ready.ToString());
        Assert.Equal(JsonValueKind.True, ready.Fields["ready"]!.GetValueKind());
    }

    private static byte[] NotText() =>
    [
        .. Encoding.ASCII.GetBytes("{\"timestamp\": \""),
        0xFF, 0xFE, 0xFA,
        .. Encoding.ASCII.GetBytes("\", \"message\": \""),
        0x80, 0x81,
        .. Encoding.ASCII.GetBytes("\"}\n"),
        0xC3, 0x28, 0xA0, 0xA1,
        .. Encoding.ASCII.GetBytes("\n"),
    ];
}
