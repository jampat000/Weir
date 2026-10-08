using Weir.Core.Media;

namespace Weir.Core.Tests.Media;

/// <summary>What a person reads when ffprobe, ffmpeg or mkvmerge fail: plain words, never the tool's own text.</summary>
public sealed class ToolFailureTextTests
{
    private const string RawProbeFailure =
        "[matroska,webm @ 000001b280590a00] EBML header parsing failed\r\nC:\\Weir\\Ready\\Movies\\Reject.Film.mkv: Invalid data found when processing input";

    [Fact]
    public void An_unreadable_file_reads_as_one_plain_sentence()
    {
        var error = ProbeOutput.FailureFor(string.Empty, RawProbeFailure);

        Assert.IsType<MediaUnreadableException>(error);
        Assert.Equal("Weir couldn't read this file: it isn't a video Weir recognises, or it is damaged.", ToolFailureText.Plain(error));
        Assert.Contains("EBML header parsing failed", ToolFailureText.Technical(error), StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_of_an_unreadable_file_says_nothing_was_written()
    {
        Assert.Equal(
            "Weir couldn't read this file: it isn't a video Weir recognises, or it is damaged. It was refused before any output was written.",
            ToolFailureText.UnreadableFileRefusal);
    }

    [Theory]
    [InlineData("[h264 @ 0x55d0] some other complaint", ToolFailureText.Generic)]
    [InlineData("C:\\Media\\film.mkv: Permission denied", ToolFailureText.NotAllowed)]
    [InlineData("Error writing trailer: No space left on device", ToolFailureText.NoSpace)]
    [InlineData("Command '[ffmpeg -i x]' timed out after 120 seconds", ToolFailureText.TookTooLong)]
    [InlineData("film.mkv: Invalid data found when processing input", ToolFailureText.UnreadableFile)]
    public void A_tool_failure_other_than_unreadable_media_is_worded_by_what_it_says(string toolText, string expected)
    {
        Assert.Equal(expected, ToolFailureText.ForToolText(toolText));
    }

    [Fact]
    public void An_ffprobe_failure_that_is_not_about_the_media_carries_no_tool_text()
    {
        var error = ProbeOutput.FailureFor(string.Empty, "[h264 @ 0x55d0] some other complaint");

        Assert.IsNotType<MediaUnreadableException>(error);
        Assert.Equal(ToolFailureText.Generic, ToolFailureText.Plain(error));
    }

    [Fact]
    public void An_ffmpeg_failure_is_worded_and_its_stderr_kept_as_the_message()
    {
        var error = ProbeOutput.FfmpegFailure("Conversion failed! C:\\Work\\x.processing.mkv: No space left on device");

        Assert.Equal(ToolFailureText.NoSpace, ToolFailureText.Plain(error));
        Assert.Contains("x.processing.mkv", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_integrity_failure_says_the_file_may_still_be_arriving_without_the_tools_report()
    {
        var error = ProbeOutput.IntegrityFailure("[matroska,webm @ 0x1] File ended prematurely");

        Assert.Equal(ToolFailureText.IncompleteRead, ToolFailureText.Plain(error));
        Assert.Contains("File ended prematurely", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timeout_never_shows_the_command_line()
    {
        var timeout = new MediaToolTimeoutException(ProbeOutput.TimeoutMessage(["ffprobe", "-i", "C:\\Media\\film.mkv"], 120));

        Assert.Equal(ToolFailureText.TookTooLong, ToolFailureText.Plain(timeout));
        Assert.Contains("film.mkv", ToolFailureText.Technical(timeout), StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_Weir_wrote_is_left_as_it_is()
    {
        var error = new MediaToolException("Planned 1 audio track, output has 0.");

        Assert.Equal("Planned 1 audio track, output has 0.", ToolFailureText.Plain(error));
    }

    [Fact]
    public void Every_sentence_a_person_reads_is_free_of_tool_text()
    {
        string[] sentences =
        [
            ToolFailureText.UnreadableFile, ToolFailureText.UnreadableFileRefusal, ToolFailureText.IncompleteRead, ToolFailureText.TookTooLong,
            ToolFailureText.NoSpace, ToolFailureText.NotAllowed, ToolFailureText.MissingOrEmpty, ToolFailureText.OutputNotPublishable, ToolFailureText.Generic,
        ];

        Assert.All(sentences, sentence =>
        {
            Assert.DoesNotContain("@ 0", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("[", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain(":\\", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("ffprobe", sentence, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ffmpeg", sentence, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Technical_detail_hides_secrets_and_is_capped()
    {
        var error = new MediaToolException("failed with api_key=hunter2 " + new string('x', 3000));

        var detail = ToolFailureText.Technical(error);

        Assert.DoesNotContain("hunter2", detail, StringComparison.Ordinal);
        Assert.True(detail.Length <= 1000);
    }
}
