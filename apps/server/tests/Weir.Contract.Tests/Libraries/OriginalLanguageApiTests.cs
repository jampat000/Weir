using Weir.Contract.Tests.Harness;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Libraries;

/// <summary>
/// The original language of a title comes from Deluno's metadata service, with nothing to set up. The metadata service is
/// a fake; no test reaches the real one. A rules preview runs the same lookup a live pass runs, so it shows what the rules
/// would keep.
/// </summary>
[ContractArea("libraries")]
public sealed class OriginalLanguageApiTests
{
    [Fact]
    public async Task A_rules_preview_takes_the_original_language_from_the_metadata_service()
    {
        using var gateway = new FakeGateway();
        gateway.Knows("nosferatu", "nosferatu.jpg", tmdbId: 653, originalLanguage: "de");
        await using var film = await LibrariesPartBFilm.StartAsync(gateway);

        var original = await film.PreviewAsync();

        Assert.Equal("matched", (string)original["lookup_status"]!);
        Assert.Equal("de", (string)original["original_language"]!);
        LibrariesPartBPosters.AssertSearch(gateway.Searches()[0], "movies", "nosferatu", "1922");
    }

    [Fact]
    public async Task A_title_is_asked_about_once_however_often_its_language_is_needed()
    {
        using var gateway = new FakeGateway();
        gateway.Knows("nosferatu", "nosferatu.jpg", originalLanguage: "de");
        await using var film = await LibrariesPartBFilm.StartAsync(gateway);

        await film.PreviewAsync();
        var again = await film.PreviewAsync();

        Assert.Equal("de", (string)again["original_language"]!);
        Assert.Single(gateway.Searches());
    }

    [Fact]
    public async Task A_title_the_service_does_not_know_leaves_the_language_preferences_in_charge()
    {
        using var gateway = new FakeGateway();
        await using var film = await LibrariesPartBFilm.StartAsync(gateway);

        var original = await film.PreviewAsync();

        Assert.Equal("no_match", (string)original["lookup_status"]!);
        Assert.Contains("language preferences", (string)original["note"]!, StringComparison.Ordinal);
    }
}
