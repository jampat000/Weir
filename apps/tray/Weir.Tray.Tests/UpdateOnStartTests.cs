using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// An update that an earlier run downloaded and never installed (Windows ended the session, or the tray was killed)
/// is installed on the next start, before the server starts, and only when the person's update choice allows it (#857).
/// </summary>
public sealed class UpdateOnStartTests : IDisposable
{
    private const string LeftWaiting = "3.3.0";

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();

    public void Dispose() => _home.Dispose();

    [Fact]
    public void An_update_left_waiting_is_installed_and_the_tray_ends_at_once()
    {
        var service = new FakeUpdateService { LeftWaitingVersion = LeftWaiting };

        var handedOver = TryApply(service, UpdateMode.Auto, () => { });

        Assert.True(handedOver);
        Assert.Equal([FakeUpdateService.AppliedLeftWaiting], service.Applied);
    }

    [Fact]
    public void An_update_left_waiting_is_installed_when_the_mode_is_download_only()
    {
        var service = new FakeUpdateService { LeftWaitingVersion = LeftWaiting };

        Assert.True(TryApply(service, UpdateMode.DownloadOnly, () => { }));
    }

    [Fact]
    public void A_server_left_running_by_a_killed_tray_is_stopped_before_the_install_starts()
    {
        var service = new FakeUpdateService { LeftWaitingVersion = LeftWaiting };
        var order = new List<string>();
        service.OnApply = call => order.Add(call);

        TryApply(service, UpdateMode.Auto, () => order.Add("orphans stopped"));

        Assert.Equal(["orphans stopped", FakeUpdateService.AppliedLeftWaiting], order);
    }

    [Fact]
    public void Nothing_is_installed_when_no_update_was_left_waiting()
    {
        var service = new FakeUpdateService();

        var handedOver = TryApply(service, UpdateMode.Auto, () => { });

        Assert.False(handedOver);
        Assert.Empty(service.Applied);
    }

    [Fact]
    public void A_person_who_chose_to_be_notified_only_never_gets_an_install_at_start_up()
    {
        var service = new FakeUpdateService { LeftWaitingVersion = LeftWaiting };

        var handedOver = TryApply(service, UpdateMode.NotifyOnly, () => { });

        Assert.False(handedOver);
        Assert.Empty(service.Applied);
    }

    [Fact]
    public void An_update_whose_install_at_start_up_did_not_finish_is_not_tried_again()
    {
        var service = new FakeUpdateService { LeftWaitingVersion = LeftWaiting };
        TryApply(service, UpdateMode.Auto, () => { });

        var secondStart = new FakeUpdateService { LeftWaitingVersion = LeftWaiting };
        var handedOver = TryApply(secondStart, UpdateMode.Auto, () => { });

        Assert.False(handedOver);
        Assert.Empty(secondStart.Applied);
    }

    [Fact]
    public void A_newer_update_left_waiting_after_a_failed_one_is_tried()
    {
        TryApply(new FakeUpdateService { LeftWaitingVersion = LeftWaiting }, UpdateMode.Auto, () => { });

        var newer = new FakeUpdateService { LeftWaitingVersion = "3.3.1" };
        var handedOver = TryApply(newer, UpdateMode.Auto, () => { });

        Assert.True(handedOver);
    }

    [Fact]
    public void An_install_is_not_started_when_the_record_of_attempts_cannot_be_used()
    {
        var service = new FakeUpdateService { LeftWaitingVersion = LeftWaiting };
        var blocked = Path.Combine(_home.Path, UpdateOnStart.AttemptFileName);
        Directory.CreateDirectory(blocked);

        var handedOver = TryApply(service, UpdateMode.Auto, () => { });

        Assert.False(handedOver);
        Assert.Empty(service.Applied);
    }

    private bool TryApply(FakeUpdateService service, UpdateMode mode, Action stopOrphanedServers) =>
        UpdateOnStart.TryApply(service, mode, _home.Path, stopOrphanedServers);
}
