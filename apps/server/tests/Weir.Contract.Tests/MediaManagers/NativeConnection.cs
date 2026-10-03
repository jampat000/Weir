using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;

namespace Weir.Contract.Tests.MediaManagers;

/// <summary>A native connection with its own secret, removed again afterwards: the connection-less native source refuses unsigned writes.</summary>
internal sealed class NativeConnection : IAsyncDisposable
{
    private readonly WeirClient _admin;
    private readonly int _connectionId;

    private NativeConnection(WeirClient admin, int connectionId, Dictionary<string, string> secretHeaders)
    {
        _admin = admin;
        _connectionId = connectionId;
        SecretHeaders = secretHeaders;
    }

    public Dictionary<string, string> SecretHeaders { get; }

    public static async Task<NativeConnection> CreateAsync(WeirClient admin)
    {
        var (row, headers) = await ManagerConnections.CreateWithSecretAsync(
            admin, new JsonObject { ["kind"] = "native", ["name"] = "Native", ["base_url"] = string.Empty, ["api_key"] = string.Empty });
        return new NativeConnection(admin, JsonFields.Id(row), headers);
    }

    public async ValueTask DisposeAsync() => await ManagerConnections.DeleteAsync(_admin, _connectionId);
}
