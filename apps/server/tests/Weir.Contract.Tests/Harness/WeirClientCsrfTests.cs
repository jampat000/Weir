using System.Net;

namespace Weir.Contract.Tests.Harness;

/// <summary>Checks the harness's own CSRF helpers against a real server, so area tests can rely on them.</summary>
[ContractArea("harness")]
public sealed class WeirClientCsrfTests(ServerFixture fixture) : IClassFixture<ServerFixture>
{
    private const string UnknownLibrary = $"{WeirClient.Api}/processing/libraries/999999";

    [Fact]
    public async Task A_delete_with_the_token_in_its_body_is_accepted_by_the_library_route()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var withToken = await admin.DeleteWithCsrfBodyAsync(UnknownLibrary);

        Assert.Equal(HttpStatusCode.NotFound, withToken.Status);
    }

    [Fact]
    public async Task A_delete_without_a_body_token_is_rejected_by_the_library_route()
    {
        using var admin = await fixture.Server.CreateAdminClientAsync();

        var withoutToken = await admin.DeleteAsync(UnknownLibrary);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, withoutToken.Status);
    }
}
