using Weir.Infrastructure.IO;
using Weir.Infrastructure.Processing;

namespace Weir.Infrastructure.Tests.Processing;

public sealed class ReleaseFolderRemovalTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Root => Directory.CreateDirectory(_dir.Join("watched")).FullName;

    private string Write(string relative, int bytes = 16)
    {
        var path = Path.Join(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void A_pack_with_another_film_keeps_the_folder_and_the_other_film()
    {
        var film = Write(Path.Join("Pack", "One.mkv"));
        var other = Write(Path.Join("Pack", "Two.mkv"));

        var removal = ReleaseFolderRemoval.Remove(Root, film, null);

        Assert.Equal(ReleaseRemovalKind.FileOnly, removal.Kind);
        Assert.Equal(ReleaseFolderRemoval.OtherVideosReason, removal.Reason);
        Assert.False(File.Exists(film));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void A_film_in_a_subfolder_of_the_release_counts_as_another_video()
    {
        var film = Write(Path.Join("Pack", "One.mkv"));
        var nested = Write(Path.Join("Pack", "Bonus", "Two.avi"));

        var removal = ReleaseFolderRemoval.Remove(Root, film, null);

        Assert.Equal(ReleaseRemovalKind.FileOnly, removal.Kind);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void An_extension_the_library_adds_counts_as_a_video()
    {
        var film = Write(Path.Join("Pack", "One.mkv"));
        var transport = Write(Path.Join("Pack", "Two.ts"));

        var removal = ReleaseFolderRemoval.Remove(Root, film, ".mkv,.ts");

        Assert.Equal(ReleaseRemovalKind.FileOnly, removal.Kind);
        Assert.True(File.Exists(transport));
    }

    [Fact]
    public void A_release_with_only_its_sample_and_extras_is_removed_whole()
    {
        var film = Write(Path.Join("Film.2024", "Film.2024.mkv"));
        Write(Path.Join("Film.2024", "Sample", "film.2024.mkv"));
        Write(Path.Join("Film.2024", "film.2024-sample.mkv"));
        Write(Path.Join("Film.2024", "Film.2024.nfo"));

        var removal = ReleaseFolderRemoval.Remove(Root, film, null);

        Assert.Equal(ReleaseRemovalKind.FolderRemoved, removal.Kind);
        Assert.False(Directory.Exists(Path.Join(Root, "Film.2024")));
    }

    [Fact]
    public void A_full_size_film_with_sample_in_its_name_still_counts()
    {
        var film = Write(Path.Join("Pack", "One.mkv"));
        var namedSample = Path.Join(Root, "Pack", "The.Sample.2019.mkv");
        using (var stream = File.Create(namedSample))
        {
            stream.SetLength(ReleaseFolderRemoval.SampleMaxBytes);
        }

        var removal = ReleaseFolderRemoval.Remove(Root, film, null);

        Assert.Equal(ReleaseRemovalKind.FileOnly, removal.Kind);
        Assert.True(File.Exists(namedSample));
    }

    [Fact]
    public void A_release_folder_reached_through_a_link_is_left_alone()
    {
        var elsewhere = Directory.CreateDirectory(_dir.Join("elsewhere", "Film")).FullName;
        var film = Path.Join(elsewhere, "Film.mkv");
        File.WriteAllBytes(film, new byte[16]);
        var linked = Path.Join(Root, "Film");
        FolderLinks.Create(linked, elsewhere);

        var removal = ReleaseFolderRemoval.Remove(Root, Path.Join(linked, "Film.mkv"), null);

        Assert.Equal(ReleaseRemovalKind.NothingRemoved, removal.Kind);
        Assert.Equal(ReleaseFolderRemoval.LinkedFolderReason, removal.Reason);
        Assert.True(File.Exists(film));
    }

    [Fact]
    public void Only_a_folder_reached_through_a_link_counts_as_linked()
    {
        var elsewhere = Directory.CreateDirectory(_dir.Join("elsewhere", "Category", "Film")).FullName;
        var linked = Path.Join(Root, "Category");
        FolderLinks.Create(linked, Path.GetDirectoryName(elsewhere)!);

        Assert.True(PathContainment.HasLinkBelowRoot(Root, Path.Join(linked, "Film")));
        Assert.False(PathContainment.HasLinkBelowRoot(Root, Directory.CreateDirectory(Path.Join(Root, "Plain")).FullName));
    }

    [Theory]
    [InlineData("Sample/film.mkv", true)]
    [InlineData("samples/film.mkv", true)]
    [InlineData("film-sample.mkv", true)]
    [InlineData("Film.2024.SAMPLE.mkv", true)]
    [InlineData("Film.Sampled.2024.mkv", false)]
    [InlineData("Film.2024.mkv", false)]
    public void Samples_are_recognised_by_folder_or_by_word(string relative, bool expected) =>
        Assert.Equal(expected, ReleaseFolderRemoval.IsSample(relative));
}
