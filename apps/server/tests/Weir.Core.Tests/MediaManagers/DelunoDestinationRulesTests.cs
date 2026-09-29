using Weir.Core.Json;
using Weir.Core.MediaManagers;

namespace Weir.Core.Tests.MediaManagers;

/// <summary><see cref="DelunoDestinationRules.ParseLibraries"/>: Deluno's download-destinations answer read into typed models.</summary>
public sealed class DelunoDestinationRulesTests
{
    private const string Answer = """
        {"libraries":[{"libraryId":"lib-1","libraryName":"Movies","mediaType":"movies","downloadsPath":"/nas/complete/movies",
          "processorOutputPath":"/nas/ready/movies","importWorkflow":"refine-before-import","somethingNew":{"a":1},
          "destinations":[{"downloadClientId":"c1","downloadClientName":"qBittorrent","protocol":"qbittorrent","category":"radarr",
            "categoryKind":"category","saveFolder":"/nas/complete/movies","clientSaveFolder":"/downloads/movies","savedBy":"deluno-per-grab",
            "matchesDownloadsPath":true,"status":"ok","message":"Saves to the downloads folder."}],
          "processorConnection":{"id":"p1","name":"Weir on RIG","pathMappings":[{"delunoPath":"/nas","processorPath":"/media"}]}}]}
        """;

    [Fact]
    public void A_full_answer_is_read_into_the_library_its_destinations_and_its_mappings_ignoring_fields_it_does_not_know()
    {
        var library = Assert.Single(DelunoDestinationRules.ParseLibraries(WireJsonParser.Parse(Answer)));

        Assert.Equal(("lib-1", "Movies", "/nas/complete/movies", "/nas/ready/movies"), (library.LibraryId, library.LibraryName, library.DownloadsPath, library.ProcessorOutputPath));
        var destination = Assert.Single(library.Destinations);
        Assert.Equal(
            new DelunoDestination("qBittorrent", "radarr", "category", "/nas/complete/movies", "deluno-per-grab", DelunoDestination.Ok, "Saves to the downloads folder."),
            destination);
        Assert.Equal(new DelunoPathMapping("/nas", "/media"), Assert.Single(library.PathMappings));
    }

    [Fact]
    public void A_library_with_no_processor_connection_and_null_folders_has_no_mappings_and_no_paths()
    {
        var payload = WireJsonParser.Parse("""
            {"libraries":[{"libraryId":"lib-2","libraryName":"TV","downloadsPath":null,"processorOutputPath":null,"destinations":[],"processorConnection":null}]}
            """);

        var library = Assert.Single(DelunoDestinationRules.ParseLibraries(payload));

        Assert.Null(library.DownloadsPath);
        Assert.Null(library.ProcessorOutputPath);
        Assert.Empty(library.Destinations);
        Assert.Empty(library.PathMappings);
    }

    [Fact]
    public void A_destination_with_no_status_reads_as_unknown_and_a_mapping_missing_a_side_is_dropped()
    {
        var payload = WireJsonParser.Parse("""
            {"libraries":[{"libraryId":"lib-3","destinations":[{"downloadClientName":"SABnzbd","category":"movies"}],
              "processorConnection":{"pathMappings":[{"delunoPath":"/a"},{"delunoPath":"/b","processorPath":"/c"}]}}]}
            """);

        var library = Assert.Single(DelunoDestinationRules.ParseLibraries(payload));

        Assert.Equal(DelunoDestination.Unknown, Assert.Single(library.Destinations).Status);
        Assert.Equal(new DelunoPathMapping("/b", "/c"), Assert.Single(library.PathMappings));
    }

    [Theory]
    [InlineData("""{"libraries":[]}""")]
    [InlineData("""{"message":"nothing here"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""{"libraries":[{"libraryName":"No id"}]}""")]
    public void An_answer_with_no_usable_library_reads_as_none(string json)
    {
        Assert.Empty(DelunoDestinationRules.ParseLibraries(WireJsonParser.Parse(json)));
    }

    [Fact]
    public void A_missing_answer_reads_as_none()
    {
        Assert.Empty(DelunoDestinationRules.ParseLibraries(null));
    }
}
