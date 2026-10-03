using Weir.Core.Configuration;
using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Tests.Platform;

public sealed class WindowsNetworkAccessTests : IDisposable
{
    private const int Port = 9347;

    private readonly TempDirectory _home = new();
    private readonly FakeFirewall _firewall = new();
    private readonly FakeAddresses _addresses = new("10.0.0.196");

    public void Dispose() => _home.Dispose();

    private sealed class FakeFirewall : IServerFirewall
    {
        public FirewallVerdict Verdict { get; set; } = FirewallVerdict.Blocks;

        public int Reads { get; private set; }

        public FirewallVerdict Judge()
        {
            Reads++;
            return Verdict;
        }
    }

    private sealed class FakeAddresses(params string[] addresses) : ILanAddresses
    {
        public IReadOnlyList<string> Read() => addresses;
    }

    private WindowsNetworkAccess Access(string host) =>
        new(new ServerListenOptions(host, Port), new LanAccessFile(_home.Path), _firewall, _addresses);

    private WindowsNetworkAccess ListeningForThisPcOnly() => Access("localhost");

    private WindowsNetworkAccess ListeningForTheNetwork() => Access("0.0.0.0");

    private void Save(NetworkScope scope) => new LanAccessFile(_home.Path).Write(scope);

    [Fact]
    public void A_server_for_this_pc_only_reports_this_pc_only_without_asking_the_firewall()
    {
        var status = ListeningForThisPcOnly().Read();

        Assert.Equal(NetworkAccessState.ThisPcOnly, status.State);
        Assert.Equal(NetworkScope.ThisPcOnly, status.Scope);
        Assert.Null(status.PendingScope);
        Assert.Equal(FirewallVerdict.NotChecked, status.Firewall);
        Assert.Equal(0, _firewall.Reads);
        Assert.Empty(status.Addresses);
    }

    [Fact]
    public void A_server_for_the_network_behind_an_allowing_firewall_is_allowed_and_names_its_addresses()
    {
        _firewall.Verdict = FirewallVerdict.Allows;

        var status = ListeningForTheNetwork().Read();

        Assert.Equal(NetworkAccessState.Allowed, status.State);
        Assert.Equal(NetworkScope.Network, status.Scope);
        Assert.Equal(["http://10.0.0.196:9347"], status.Addresses);
        Assert.Equal(Port, status.Port);
    }

    [Fact]
    public void A_server_for_the_network_behind_a_blocking_firewall_is_blocked()
    {
        var status = ListeningForTheNetwork().Read();

        Assert.Equal(NetworkAccessState.Blocked, status.State);
        Assert.Equal(FirewallVerdict.Blocks, status.Firewall);
        Assert.Null(status.PendingScope);
    }

    [Fact]
    public void A_saved_choice_the_server_has_not_caught_up_with_is_pending()
    {
        Save(NetworkScope.Network);

        var status = ListeningForThisPcOnly().Read();

        Assert.Equal(NetworkScope.ThisPcOnly, status.Scope);
        Assert.Equal(NetworkScope.Network, status.PendingScope);
        Assert.Equal(NetworkAccessState.ThisPcOnly, status.State);
        Assert.Equal(["http://10.0.0.196:9347"], status.Addresses);
    }

    [Fact]
    public void A_pending_change_to_the_network_reads_the_firewall_to_say_whether_approval_is_needed()
    {
        Save(NetworkScope.Network);
        _firewall.Verdict = FirewallVerdict.Allows;

        var status = ListeningForThisPcOnly().Read();

        Assert.Equal(FirewallVerdict.Allows, status.Firewall);
    }

    [Fact]
    public void A_saved_choice_the_server_already_follows_is_not_pending()
    {
        Save(NetworkScope.ThisPcOnly);

        Assert.Null(ListeningForThisPcOnly().Read().PendingScope);
    }

    [Fact]
    public void A_pending_change_back_to_this_pc_only_is_pending_while_the_server_still_listens_for_the_network()
    {
        Save(NetworkScope.ThisPcOnly);

        var status = ListeningForTheNetwork().Read();

        Assert.Equal(NetworkScope.ThisPcOnly, status.PendingScope);
    }

    [Fact]
    public void Choosing_saves_the_choice_for_the_tray()
    {
        ListeningForThisPcOnly().Choose(NetworkScope.Network);

        Assert.Equal(NetworkScope.Network, new LanAccessFile(_home.Path).Read());
    }

    [Fact]
    public void The_choice_can_always_be_changed_on_the_windows_package()
    {
        Assert.Null(ListeningForThisPcOnly().NotChangeableReason);
    }
}
