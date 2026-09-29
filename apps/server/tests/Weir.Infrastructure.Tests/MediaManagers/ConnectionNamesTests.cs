namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>The stored name of a connection always follows its kind and address.</summary>
public sealed class ConnectionNamesTests
{
    private static Task<List<string>> ManagerNamesAsync(MediaManagerFixture fixture) =>
        fixture.Db(async uow => (await fixture.ConnectionStore.ListAsync(uow)).Select(row => row.Name).ToList(), commit: false);

    private static Task<List<string>> ClientNamesAsync(DownloadClientFixture fixture) =>
        fixture.Db(async uow => (await fixture.ConnectionStore.ListAsync(uow)).Select(row => row.Name).ToList(), commit: false);

    [Fact]
    public async Task A_new_media_manager_is_named_after_its_kind_and_address()
    {
        using var fixture = new MediaManagerFixture();

        await fixture.AddConnectionAsync("deluno", "http://RIG:5099");

        Assert.Equal(["Deluno on RIG"], await ManagerNamesAsync(fixture));
    }

    [Fact]
    public async Task A_second_media_manager_on_the_same_host_gives_both_their_ports_and_removing_it_takes_them_away()
    {
        using var fixture = new MediaManagerFixture();
        await fixture.AddConnectionAsync("radarr", "http://nas:7878");
        var second = await fixture.AddConnectionAsync("radarr", "http://nas:7879");

        Assert.Equal(["Radarr on nas (7878)", "Radarr on nas (7879)"], await ManagerNamesAsync(fixture));

        await fixture.Db(async uow =>
        {
            await fixture.ConnectionStore.DeleteAsync(uow, second);
            return 0;
        });

        Assert.Equal(["Radarr on nas"], await ManagerNamesAsync(fixture));
    }

    [Fact]
    public async Task Moving_a_media_manager_to_another_host_renames_it()
    {
        using var fixture = new MediaManagerFixture();
        var id = await fixture.AddConnectionAsync("sonarr", "http://nas:8989");
        var row = await fixture.Db(uow => fixture.ConnectionStore.GetAsync(uow, id));

        await fixture.Db(async uow =>
        {
            await fixture.Connections.UpdateAsync(uow, row!, baseUrl: "http://10.0.0.9:8989");
            return 0;
        });

        Assert.Equal(["Sonarr on 10.0.0.9"], await ManagerNamesAsync(fixture));
    }

    [Fact]
    public async Task Refreshing_replaces_the_typed_names_media_managers_were_saved_under()
    {
        using var fixture = new MediaManagerFixture();
        await fixture.Store.Execute(
            "INSERT INTO media_manager_connections (kind, name, enabled, base_url) VALUES " +
            "('radarr', 'Movies', 1, 'http://nas:7878'), ('radarr', '4K movies', 1, 'http://nas:7879'), ('deluno', 'Deluno', 1, 'http://RIG:5099')");

        await fixture.Db(async uow =>
        {
            await fixture.ConnectionStore.RefreshNamesAsync(uow);
            return 0;
        });

        Assert.Equal(["Radarr on nas (7878)", "Radarr on nas (7879)", "Deluno on RIG"], await ManagerNamesAsync(fixture));
    }

    [Fact]
    public async Task Refreshing_can_swap_two_names_a_unique_column_would_refuse_to_swap_directly()
    {
        using var fixture = new MediaManagerFixture();
        await fixture.Store.Execute(
            "INSERT INTO media_manager_connections (kind, name, enabled, base_url) VALUES " +
            "('radarr', 'Radarr on nas (7879)', 1, 'http://nas:7878'), ('radarr', 'Radarr on nas (7878)', 1, 'http://nas:7879')");

        await fixture.Db(async uow =>
        {
            await fixture.ConnectionStore.RefreshNamesAsync(uow);
            return 0;
        });

        Assert.Equal(["Radarr on nas (7878)", "Radarr on nas (7879)"], await ManagerNamesAsync(fixture));
    }

    [Fact]
    public async Task Refreshing_leaves_names_that_are_already_right_and_their_rows_untouched()
    {
        using var fixture = new MediaManagerFixture();
        await fixture.AddConnectionAsync("radarr", "http://nas:7878");
        var before = await fixture.Store.Scalar("SELECT count(*) FROM media_manager_connections WHERE updated_at = created_at");

        await fixture.Db(async uow =>
        {
            await fixture.ConnectionStore.RefreshNamesAsync(uow);
            return 0;
        });

        Assert.Equal(["Radarr on nas"], await ManagerNamesAsync(fixture));
        Assert.Equal(before, await fixture.Store.Scalar("SELECT count(*) FROM media_manager_connections WHERE updated_at = created_at"));
    }

    [Fact]
    public async Task A_new_download_client_is_named_after_its_kind_and_address()
    {
        using var fixture = new DownloadClientFixture();

        await fixture.AddConnectionAsync("qbittorrent", "http://10.0.0.51:8080");

        Assert.Equal(["qBittorrent on 10.0.0.51"], await ClientNamesAsync(fixture));
    }

    [Fact]
    public async Task Two_download_clients_of_one_kind_on_the_same_host_carry_their_ports()
    {
        using var fixture = new DownloadClientFixture();
        await fixture.AddConnectionAsync("sabnzbd", "http://nas:8080", apiKey: "key");
        await fixture.AddConnectionAsync("sabnzbd", "http://nas:8081", apiKey: "key");

        Assert.Equal(["SABnzbd on nas (8080)", "SABnzbd on nas (8081)"], await ClientNamesAsync(fixture));
    }

    [Fact]
    public async Task Refreshing_replaces_the_typed_names_download_clients_were_saved_under()
    {
        using var fixture = new DownloadClientFixture();
        await fixture.Store.Execute(
            "INSERT INTO download_client_connections (kind, name, enabled, base_url) VALUES ('qbittorrent', 'Living room', 1, 'http://10.0.0.51:8080')");

        await fixture.Db(async uow =>
        {
            await fixture.ConnectionStore.RefreshNamesAsync(uow);
            return 0;
        });

        Assert.Equal(["qBittorrent on 10.0.0.51"], await ClientNamesAsync(fixture));
    }
}
