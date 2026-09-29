using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Weir.Host;
using Weir.Infrastructure.Tests;

namespace Weir.Api.Tests;

/// <summary>The Windows tray stops the server by setting a named event it opens by the server's process id.</summary>
public sealed class StopRequestTests
{
    private static readonly TimeSpan StopCeiling = TimeSpan.FromSeconds(30);

    [Fact]
    public void The_stop_event_is_named_after_the_process_id_in_the_users_session()
    {
        Assert.Equal(@"Local\Weir-Stop-4242", StopRequest.EventName(4242));
    }

    [WindowsFact("The tray's stop request is a Windows named event.")]
    [SupportedOSPlatform("windows")]
    public async Task Setting_the_stop_event_stops_the_server()
    {
        var eventName = StopRequest.EventName(Environment.ProcessId) + "-" + Guid.NewGuid().ToString("N");
        await using var server = await WeirTestServer.StartAsync(configureServices: services => services.AddStopRequestListener(eventName));
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        using (var stopEvent = EventWaitHandle.OpenExisting(eventName))
        {
            stopEvent.Set();
        }

        await stopping.Task.WaitAsync(StopCeiling);
        Assert.True(server.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }
}
