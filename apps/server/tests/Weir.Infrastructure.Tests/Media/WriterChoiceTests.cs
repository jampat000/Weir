using Weir.Core.Media;
using Weir.Infrastructure.Media;
using Weir.Infrastructure.Tests.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.Media;

/// <summary>Which tool a workflow's "Writes files with" choice gives each file, for new downloads and library cleaning alike.</summary>
public sealed class WriterChoiceTests
{
    private static MediaTools Tools(string? mkvmerge) =>
        new(new FakeMediaRunner(), new FixedResolver(mkvmerge: mkvmerge), new ListLogger<MediaTools>(), TimeProvider.System);

    [Fact]
    public void Best_gives_a_matroska_file_to_mkvmerge_when_it_is_installed()
    {
        var writer = Tools("mkvmerge").WriterFor("film.mkv", RemuxWriterChoice.Best);

        Assert.IsType<MkvmergeRemuxWriter>(writer);
    }

    [Fact]
    public void Best_gives_a_file_mkvmerge_cannot_write_to_ffmpeg()
    {
        var writer = Tools("mkvmerge").WriterFor("film.mp4", RemuxWriterChoice.Best);

        Assert.IsType<FfmpegRemuxWriter>(writer);
    }

    [Fact]
    public void Best_gives_every_file_to_ffmpeg_when_mkvmerge_is_not_installed()
    {
        var writer = Tools(mkvmerge: null).WriterFor("film.mkv", RemuxWriterChoice.Best);

        Assert.IsType<FfmpegRemuxWriter>(writer);
    }

    [Fact]
    public void Choosing_ffmpeg_gives_every_file_to_ffmpeg()
    {
        var writer = Tools("mkvmerge").WriterFor("film.mkv", RemuxWriterChoice.Ffmpeg);

        Assert.IsType<FfmpegRemuxWriter>(writer);
    }
}
