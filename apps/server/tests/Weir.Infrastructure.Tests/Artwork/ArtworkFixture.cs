using System.Net;
using System.Net.Http.Headers;
using System.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Infrastructure.Artwork;
using Weir.Infrastructure.Sqlite;
using Weir.Infrastructure.Tests.MediaManagers;
using Weir.Infrastructure.Tests.Platform;

namespace Weir.Infrastructure.Tests.Artwork;

/// <summary>
/// The poster services over a database at head, with a scripted stand-in for the metadata service. No test reaches the
/// real service: the gateway address is a made-up host whose requests the stand-in answers.
/// </summary>
internal sealed class ArtworkFixture : IDisposable
{
    public const string GatewayUrl = "http://gateway.test";
    public static readonly byte[] ImageBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    public ArtworkFixture()
    {
        Store = new StoreFixture(("WEIR_ARTWORK_GATEWAY_URL", GatewayUrl));
        Http = new FakeManagerHttp();
        Lookups = new ArtworkLookupStore();
        Files = new ArtworkFileStore();
        Subjects = new ArtworkSubjects(Lookups, Files);
        PosterFiles = new ArtworkPosterFiles(Store.Options);
        Limiter = new ArtworkRateLimiter(Store.Clock);
        Gateway = new ArtworkGatewayClient(Store.Options, Http, Store.Clock, NullLogger<ArtworkGatewayClient>.Instance);
        PosterUrls = new ArtworkPosterUrls(Files, Gateway);
        Resolver = new ArtworkResolver(Store.Database, Lookups, Gateway, PosterFiles, Limiter, Store.Clock, NullLogger<ArtworkResolver>.Instance);
        Discovery = new ArtworkDiscovery(Store.Database, Files, Subjects);
        Pruner = new ArtworkPruner(Store.Database, PosterFiles, Store.Clock, NullLogger<ArtworkPruner>.Instance);
        OriginalLanguages = new GatewayOriginalLanguageLookup(Store.Database, Lookups, Files, Subjects, Resolver, Gateway, Limiter, Store.Clock);
    }

    public StoreFixture Store { get; }

    public FakeManagerHttp Http { get; }

    public ArtworkLookupStore Lookups { get; }

    public ArtworkFileStore Files { get; }

    public ArtworkSubjects Subjects { get; }

    public ArtworkPosterUrls PosterUrls { get; }

    public ArtworkPosterFiles PosterFiles { get; }

    public ArtworkRateLimiter Limiter { get; }

    public ArtworkGatewayClient Gateway { get; }

    public ArtworkResolver Resolver { get; }

    public ArtworkDiscovery Discovery { get; }

    public ArtworkPruner Pruner { get; }

    public GatewayOriginalLanguageLookup OriginalLanguages { get; }

    public ArtworkResolverTask TaskFor(ArtworkGatewayClient gateway) => new(gateway, Discovery, Resolver, Pruner, Store.Clock);

    /// <summary>A client for a server whose administrator set <c>WEIR_ARTWORK_GATEWAY_URL=off</c>.</summary>
    public ArtworkGatewayClient SwitchedOffGateway() => new(
        Core.Configuration.WeirOptionsLoader.Load(new Core.Configuration.RuntimeEnvironment(
            new Dictionary<string, string> { ["WEIR_HOME"] = Store.Home.Path, ["WEIR_ARTWORK_GATEWAY_URL"] = "off" },
            OperatingSystem.IsWindows(),
            Store.Home.Path,
            Store.Home.Path)),
        Http,
        Store.Clock,
        NullLogger<ArtworkGatewayClient>.Instance);

    /// <summary>The id of the library the database is seeded with for a kind of media.</summary>
    public Task<long> LibraryIdAsync(string mediaType) =>
        Store.Scalar($"SELECT id FROM libraries WHERE media_type = '{mediaType}' ORDER BY display_order, id LIMIT 1");

    /// <summary>What the metadata service answers to a search: one result with a poster, as the real one words it.</summary>
    public static string SearchAnswer(string posterFile, long tmdbId = 603, string originalLanguage = "en") =>
        $$"""{"provider":"deluno-broker","results":[{"provider":"tmdb","providerId":"{{tmdbId}}","title":"x","originalLanguage":"{{originalLanguage}}","posterUrl":"{{GatewayUrl}}/artwork/w780/{{posterFile}}"}]}""";

    public ArtworkFixture ServeSearch(string answerJson)
    {
        Http.Json(HttpMethod.Get, "/metadata/search", answerJson);
        return this;
    }

    public ArtworkFixture ServeImage(string posterFile)
    {
        Http.Route(HttpMethod.Get, $"/artwork/w342/{posterFile}", _ => Image());
        return this;
    }

    public static HttpResponseMessage Image()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ImageBytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return response;
    }

    public List<RecordedRequest> Searches() => [.. Http.Requests.Where(request => request.Uri.AbsolutePath == "/metadata/search")];

    public static string QueryValue(RecordedRequest request, string name) => HttpUtility.ParseQueryString(request.Uri.Query)[name] ?? string.Empty;

    /// <summary>Give a file in a library its title as the resolver would, from its name.</summary>
    public async Task QueueFileAsync(long libraryId, string path, string mediaScope = "movie", int priority = ArtworkPriority.Processing) =>
        await Store.WithUnitOfWork(async uow =>
        {
            await Subjects.LinkFromNameAsync(uow, new ArtworkCandidate(libraryId, path, mediaScope, null), priority);
            return 0;
        });

    public Task<string> OutcomeAsync(string lookupKey) =>
        Store.WithUnitOfWork(async uow => Convert.ToString(await uow.ScalarAsync("SELECT outcome FROM artwork_lookups WHERE lookup_key = $key", ("$key", lookupKey)), System.Globalization.CultureInfo.InvariantCulture)!, commit: false);

    /// <summary>The original language kept for a title: null before the service has answered, empty when it answered with none.</summary>
    public Task<string?> OriginalLanguageAsync(string lookupKey) =>
        Store.WithUnitOfWork(
            async uow => await uow.ScalarAsync("SELECT original_language FROM artwork_lookups WHERE lookup_key = $key", ("$key", lookupKey)) is string language ? language : null,
            commit: false);

    /// <summary>A title's lookup as the resolver and the original-language lookup read it.</summary>
    public Task<ArtworkLookup?> LookupAsync(string lookupKey) =>
        Store.WithUnitOfWork(uow => Lookups.FindAsync(uow, lookupKey, Store.Clock.GetUtcNow()), commit: false);

    public Task<IReadOnlyDictionary<(long LibraryId, string Path), string>> PosterUrlsForAsync(long libraryId, params string[] paths) =>
        Store.WithUnitOfWork(uow => PosterUrls.ForFilesAsync(uow, paths.Select(path => (libraryId, path))), commit: false);

    public Task InTransactionAsync(Func<UnitOfWork, Task> work) =>
        Store.WithUnitOfWork(async uow =>
        {
            await work(uow);
            return 0;
        });

    public Task InsertFileAsync(long libraryId, string path) =>
        Store.Execute($"INSERT INTO files (library_id, relative_path, status) VALUES ({libraryId}, '{path.Replace("'", "''", StringComparison.Ordinal)}', 'processed')");

    public void Dispose()
    {
        Resolver.Dispose();
        Store.Dispose();
    }
}
