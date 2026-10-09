using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>Opening Weir from the icon, a menu item or a balloon: once per click, and only when there is something to open.</summary>
public sealed class WeirOpenerTests : IDisposable
{
    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly FakeTimeProvider _clock = new();
    private readonly List<(int Port, string Path)> _browser = [];
    private readonly List<(string Text, ToolTipIcon Icon, Action? Click)> _balloons = [];
    private readonly List<string> _restarts = [];
    private ServerPhase _phase = ServerPhase.Running;

    public void Dispose() => _home.Dispose();

    private WeirOpener Opener() => new(
        () => _phase,
        () => 9400,
        new Debounce(TimeSpan.FromMilliseconds(1250), _clock),
        (port, path) =>
        {
            _browser.Add((port, path));
            return true;
        },
        (_, text, icon, click) => _balloons.Add((text, icon, click)),
        () => _restarts.Add("restart"));

    [Fact]
    public void A_click_opens_Weir_at_the_live_port()
    {
        Opener().OpenFromClick("tray-click");

        Assert.Equal([(9400, "/")], _browser);
        Assert.Empty(_balloons);
    }

    [Fact]
    public void A_click_and_the_double_click_that_follows_it_open_one_window()
    {
        var opener = Opener();

        opener.OpenFromClick("tray-click");
        _clock.Advance(TimeSpan.FromMilliseconds(120));
        opener.OpenFromClick("tray-dblclick");

        Assert.Single(_browser);
    }

    [Fact]
    public void A_double_click_on_its_own_opens_one_window_and_a_later_click_another()
    {
        var opener = Opener();

        opener.OpenFromClick("tray-dblclick");
        _clock.Advance(TimeSpan.FromSeconds(3));
        opener.OpenFromClick("tray-click");

        Assert.Equal(2, _browser.Count);
    }

    [Fact]
    public void A_menu_click_and_a_balloon_click_inside_the_window_open_one_window()
    {
        var opener = Opener();

        opener.OpenFromClick("tray");
        opener.Open("start-balloon");

        Assert.Single(_browser);
    }

    [Fact]
    public void A_page_of_Weir_opens_at_its_path()
    {
        Opener().Open("tray-update-balloon", Program.UpdateCheckPath);

        Assert.Equal([(9400, "/system?tab=about")], _browser);
    }

    [Fact]
    public void The_already_running_balloon_gives_the_address_and_a_click_opens_Weir()
    {
        Opener().ShowAlreadyRunning();

        var balloon = Assert.Single(_balloons);
        Assert.Equal("Weir is already running at http://localhost:9400. Click to open it.", balloon.Text);
        Assert.Equal(ToolTipIcon.Info, balloon.Icon);
        Assert.Empty(_browser);
        balloon.Click!();
        Assert.Equal([(9400, "/")], _browser);
    }

    [Fact]
    public void While_the_server_starts_a_click_says_so_and_opens_nothing()
    {
        _phase = ServerPhase.Starting;

        Opener().OpenFromClick("tray-click");

        Assert.Empty(_browser);
        var balloon = Assert.Single(_balloons);
        Assert.Equal(TrayBalloons.StillStartingText, balloon.Text);
        Assert.Null(balloon.Click);
    }

    [Fact]
    public void When_the_server_is_stopped_a_click_offers_the_restart_and_opens_nothing()
    {
        _phase = ServerPhase.Stopped;

        Opener().OpenFromClick("tray-click");

        Assert.Empty(_browser);
        var balloon = Assert.Single(_balloons);
        Assert.Equal(TrayBalloons.NotRunningText, balloon.Text);
        Assert.Equal(ToolTipIcon.Warning, balloon.Icon);
        balloon.Click!();
        Assert.Single(_restarts);
    }
}
