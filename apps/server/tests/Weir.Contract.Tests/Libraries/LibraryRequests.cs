using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using static Weir.Contract.Tests.Libraries.LibrariesPartAHelpers;

namespace Weir.Contract.Tests.Libraries;

/// <summary>Requests to the Processing libraries routes that several tests make the same way.</summary>
public static class LibraryRequests
{
    public const string LibrariesUrl = Api + "/processing/libraries";
    public const string RuleSets = Api + "/processing/rule-sets";

    /// <summary>POSTs <paramref name="defaults"/> with <paramref name="overrides"/> replacing or adding fields.</summary>
    public static Task<WeirResponse> CreateAsync(WeirClient client, JsonObject defaults, params (string Name, JsonNode? Value)[] overrides)
    {
        foreach (var (name, value) in overrides)
        {
            defaults[name] = value;
        }

        return client.PostWithCsrfAsync(LibrariesUrl, defaults);
    }

    /// <summary>Leaves only the seeded libraries, so each test starts the same way.</summary>
    public static async Task RemoveAddedLibrariesAsync(WeirClient client)
    {
        foreach (var row in (await client.GetAsync(LibrariesUrl)).Elements)
        {
            if ((string)row!["name"]! is not ("Movies" or "TV"))
            {
                (await client.DeleteWithCsrfBodyAsync($"{LibrariesUrl}/{(long)row["id"]!}")).ShouldBe(HttpStatusCode.NoContent);
            }
        }
    }
}
