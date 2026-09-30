using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary>A connection is named after what it is and where it runs, never after something typed.</summary>
public sealed class ConnectionNamingTests
{
    private static string NameOf(string product, string baseUrl) =>
        ConnectionNaming.NamesFor([new ConnectionAddress(1, product, baseUrl)])[1];

    [Theory]
    [InlineData("Deluno", "http://RIG:5099", "Deluno on RIG")]
    [InlineData("Radarr", "http://nas:7878", "Radarr on nas")]
    [InlineData("Radarr", "https://media.example.lan/radarr", "Radarr on media.example.lan")]
    [InlineData("qBittorrent", "http://10.1.1.51:8080", "qBittorrent on 10.1.1.51")]
    [InlineData("Sonarr", "http://192.168.0.9", "Sonarr on 192.168.0.9")]
    [InlineData("Radarr", "http://[::1]:7878", "Radarr on [::1]")]
    public void A_name_is_the_product_and_the_host_as_written(string product, string baseUrl, string expected)
    {
        Assert.Equal(expected, NameOf(product, baseUrl));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    public void A_connection_with_no_usable_address_is_named_after_its_product(string baseUrl)
    {
        Assert.Equal("Radarr", NameOf("Radarr", baseUrl));
    }

    [Fact]
    public void Two_connections_of_one_product_on_one_host_carry_their_ports()
    {
        var names = ConnectionNaming.NamesFor(
        [
            new ConnectionAddress(1, "Radarr", "http://NAS:7878"),
            new ConnectionAddress(2, "Radarr", "http://nas:7879"),
        ]);

        Assert.Equal(["Radarr on NAS (7878)", "Radarr on nas (7879)"], [names[1], names[2]]);
    }

    [Fact]
    public void A_connection_on_a_port_left_out_of_its_address_carries_the_ports_the_scheme_implies()
    {
        var names = ConnectionNaming.NamesFor(
        [
            new ConnectionAddress(1, "Radarr", "http://nas"),
            new ConnectionAddress(2, "Radarr", "https://nas"),
        ]);

        Assert.Equal(["Radarr on nas (80)", "Radarr on nas (443)"], [names[1], names[2]]);
    }

    [Fact]
    public void Connections_behind_one_address_carry_the_path_that_tells_them_apart()
    {
        var names = ConnectionNaming.NamesFor(
        [
            new ConnectionAddress(1, "Radarr", "https://media.lan/radarr"),
            new ConnectionAddress(2, "Radarr", "https://media.lan/radarr4k"),
        ]);

        Assert.Equal(["Radarr on media.lan (443/radarr)", "Radarr on media.lan (443/radarr4k)"], [names[1], names[2]]);
    }

    [Fact]
    public void Different_products_or_hosts_never_carry_a_port()
    {
        var names = ConnectionNaming.NamesFor(
        [
            new ConnectionAddress(1, "Radarr", "http://nas:7878"),
            new ConnectionAddress(2, "Sonarr", "http://nas:8989"),
            new ConnectionAddress(3, "Radarr", "http://other:7878"),
        ]);

        Assert.Equal(["Radarr on nas", "Sonarr on nas", "Radarr on other"], [names[1], names[2], names[3]]);
    }

    [Fact]
    public void Identical_connections_and_address_less_ones_are_numbered_so_no_name_repeats()
    {
        var names = ConnectionNaming.NamesFor(
        [
            new ConnectionAddress(1, "Media manager", string.Empty),
            new ConnectionAddress(2, "Media manager", string.Empty),
            new ConnectionAddress(3, "Radarr", "http://nas:7878"),
            new ConnectionAddress(4, "Radarr", "http://nas:7878"),
        ]);

        Assert.Equal(["Media manager", "Media manager (2)", "Radarr on nas (7878)", "Radarr on nas (7878) (2)"], [names[1], names[2], names[3], names[4]]);
    }

    [Fact]
    public void The_same_connections_get_the_same_names_in_any_order()
    {
        ConnectionAddress[] connections =
        [
            new(1, "Radarr", "http://nas:7878"),
            new(2, "Radarr", "http://nas:7879"),
            new(3, "Deluno", "http://rig:5099"),
        ];

        var forward = ConnectionNaming.NamesFor(connections);
        var backward = ConnectionNaming.NamesFor([.. connections.Reverse()]);

        Assert.Equal(forward.OrderBy(pair => pair.Key), backward.OrderBy(pair => pair.Key));
    }

    [Theory]
    [InlineData("deluno", "Main", "Deluno (Main)")]
    [InlineData("radarr", "4K", "Radarr (4K)")]
    [InlineData("deluno", "Deluno", "Deluno")]
    [InlineData("deluno", "Deluno on RIG", "Deluno on RIG")]
    [InlineData("radarr", "radarr on nas (7879)", "radarr on nas (7879)")]
    [InlineData("native", "Home", "Media manager (Home)")]
    [InlineData("radarr", "", "Radarr")]
    public void A_manager_label_leads_with_the_product_unless_the_name_already_does(string kind, string name, string expected)
    {
        Assert.Equal(expected, MediaManagerKinds.LabelForConnection(kind, name));
    }

    [Theory]
    [InlineData("qbittorrent", "qBittorrent on 10.1.1.51", "qBittorrent on 10.1.1.51")]
    [InlineData("sabnzbd", "Living room", "SABnzbd (Living room)")]
    [InlineData("nzbget", "", "NZBGet")]
    public void A_download_client_label_leads_with_the_product_unless_the_name_already_does(string kind, string name, string expected)
    {
        Assert.Equal(expected, DownloadClientKinds.LabelForConnection(kind, name));
    }

    [Theory]
    [InlineData("radarr", "Radarr on nas", "4K", "Radarr on nas · 4K")]
    [InlineData("radarr", "Radarr on nas (7879)", "Kids", "Radarr on nas (7879) · Kids")]
    [InlineData("deluno", "Main", "4K", "Deluno (Main) · 4K")]
    [InlineData("radarr", "Radarr on nas", null, "Radarr on nas")]
    [InlineData("radarr", "Radarr on nas", "   ", "Radarr on nas")]
    [InlineData("radarr", "", "4K", "Radarr · 4K")]
    public void A_manager_nickname_follows_its_label(string kind, string name, string? nickname, string expected)
    {
        Assert.Equal(expected, MediaManagerKinds.LabelForConnection(kind, name, nickname));
    }

    [Fact]
    public void A_download_client_nickname_follows_its_label()
    {
        Assert.Equal("qBittorrent on nas · Seedbox", DownloadClientKinds.LabelForConnection("qbittorrent", "qBittorrent on nas", "Seedbox"));
    }

    [Fact]
    public void A_connection_that_carries_a_nickname_reads_it_in_its_label()
    {
        var manager = new ManagerConnection("radarr", "Radarr on nas", "http://nas:7878", "key", 1, "4K");
        var client = new DownloadClientConnection("sabnzbd", "SABnzbd on nas", "http://nas:8080", null, null, "key", 1, "Usenet");

        Assert.Equal(("Radarr on nas · 4K", "SABnzbd on nas · Usenet"), (manager.Label, client.Label));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  	 ", null)]
    [InlineData("  4K  ", "4K")]
    public void A_nickname_is_trimmed_and_a_blank_one_is_none(string? typed, string? expected)
    {
        Assert.Equal(expected, ConnectionNicknames.Normalize(typed));
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void A_nickname_over_the_limit_does_not_fit(int length, bool tooLong)
    {
        Assert.Equal(!tooLong, ConnectionNicknames.Fits(new string('a', length)));
    }

    [Theory]
    [InlineData(null, null, false, null)]
    [InlineData("4K", null, false, null)]
    [InlineData("4K", "4K", false, "4K")]
    [InlineData("4K", " 4K ", false, "4K")]
    [InlineData(null, "4K", true, "4K")]
    [InlineData("4K", "Kids", true, "Kids")]
    [InlineData("4K", "", true, null)]
    public void An_update_changes_the_nickname_only_when_it_asks_for_a_different_one(string? current, string? requested, bool changes, string? wanted)
    {
        var changed = ConnectionNicknames.TryChange(current, requested, out var actual);

        Assert.Equal(changes, changed);
        if (changed)
        {
            Assert.Equal(wanted, actual);
        }
    }
}
