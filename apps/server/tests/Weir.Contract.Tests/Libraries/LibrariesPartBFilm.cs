using System.Net;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Libraries;

/// <summary>A server with one library holding one film, and a profile that keeps the title's original language.</summary>
internal sealed class LibrariesPartBFilm : IAsyncDisposable
{
    public const string Path = "Nosferatu (1922)/Nosferatu.1922.1080p.mkv";

    private readonly FakeFfmpeg _tools;
    private readonly TemporaryFolder _folders;
    private readonly WeirServer _server;
    private readonly WeirClient _admin;
    private readonly long _libraryId;
    private readonly long _profileId;

    private LibrariesPartBFilm(FakeFfmpeg tools, TemporaryFolder folders, WeirServer server, WeirClient admin, long libraryId, long profileId)
    {
        _tools = tools;
        _folders = folders;
        _server = server;
        _admin = admin;
        _libraryId = libraryId;
        _profileId = profileId;
    }

    public static async Task<LibrariesPartBFilm> StartAsync(FakeGateway gateway)
    {
        var tools = FakeFfmpeg.Install();
        var folders = new TemporaryFolder();
        WeirServer? server = null;
        WeirClient? admin = null;
        try
        {
            server = await WeirServer.StartNewAsync(tools.Env.With(gateway.Env).With(("WEIR_PROCESSING_WORKER_COUNT", "0")));
            admin = await server.CreateAdminClientAsync();
            var library = await LibrariesPartBLibraries.CreateAsync(admin, folders.Path);
            var source = System.IO.Path.Combine(folders.Path, "watched", Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(source)!);
            await File.WriteAllBytesAsync(source, FakeMedia.Bytes(FakeMedia.Probe(audioLanguages: ["eng", "ger"])));
            var profile = await admin.PostWithCsrfAsync(
                $"{WeirClient.Api}/processing/rule-sets",
                new JsonObject { ["name"] = "Keep the original", ["primary_audio_lang"] = "eng", ["keep_original_language"] = true });
            LibrariesPartBChecks.Status(profile, HttpStatusCode.Created);
            return new LibrariesPartBFilm(tools, folders, server, admin, (long)library["id"]!, (long)profile.Fields["id"]!);
        }
        catch
        {
            admin?.Dispose();
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            tools.Dispose();
            folders.Dispose();
            throw;
        }
    }

    /// <summary>What a rules preview of the film says about the title's original language.</summary>
    public async Task<JsonObject> PreviewAsync()
    {
        var previewed = await _admin.PostWithCsrfAsync(
            $"{WeirClient.Api}/processing/libraries/{_libraryId}/preview",
            new JsonObject { ["relative_path"] = Path, ["rule_set_id"] = _profileId });
        LibrariesPartBChecks.Status(previewed, HttpStatusCode.OK);
        return previewed.Fields["original_language"]!.AsObject();
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _server.DisposeAsync();
        _tools.Dispose();
        _folders.Dispose();
    }
}
