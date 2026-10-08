using System.Text.Json;
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
        Assert.Equal("Weir couldn't read this file: it isn't a video Weir recognises, or it is damaged or incomplete.", ToolFailureText.Plain(error));
        Assert.Contains("EBML header parsing failed", ToolFailureText.Technical(error), StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_of_an_unreadable_file_says_how_long_it_waited_and_that_nothing_was_written()
    {
        Assert.Equal(
            "Weir couldn't read this file: it isn't a video Weir recognises, or it is damaged or incomplete. " +
            "Weir looked at it 4 times over about 65 minutes and it did not change. It was refused before any output was written.",
            ToolFailureText.UnreadableFileRefusal(4, 65));
    }

    [Theory]
    [InlineData("The End of File.mkv: Permission denied", ToolFailureText.NotAllowed)]
    [InlineData("C:\\Media\\Invalid data found when processing input.mkv: Permission denied", ToolFailureText.NotAllowed)]
    [InlineData("C:\\Media\\Moov Atom Not Found.mp4: No space left on device", ToolFailureText.NoSpace)]
    [InlineData("The End of File.mkv: Invalid data found when processing input", ToolFailureText.UnreadableFile)]
    [InlineData("[mov,mp4,m4a,3gp,3g2,mj2 @ 0x1] moov atom not found\nfilm.mp4: Invalid data found when processing input", ToolFailureText.UnreadableFile)]
    public void A_marker_in_the_name_of_the_file_is_not_the_tool_saying_it(string toolText, string expected)
    {
        Assert.Equal(expected, ToolFailureText.ForToolText(toolText));
    }

    [Fact]
    public void A_file_named_like_a_marker_is_not_marked_unreadable_unless_the_tool_says_so()
    {
        var denied = ProbeOutput.FailureFor(string.Empty, "C:\\Media\\The End of File.mkv: Permission denied");
        var unreadable = ProbeOutput.FailureFor(string.Empty, "C:\\Media\\The End of File.mkv: Invalid data found when processing input");

        Assert.IsNotType<MediaUnreadableException>(denied);
        Assert.Equal(ToolFailureText.NotAllowed, ToolFailureText.Plain(denied));
        Assert.IsType<MediaUnreadableException>(unreadable);
    }

    [Fact]
    public void Every_message_a_check_of_the_output_throws_carries_a_sentence_for_a_person()
    {
        var wrongAudio = Assert.Throws<MediaToolException>(() => ProbeOutput.ValidateRemuxOutput(
            JsonDocument.Parse("""{"streams":[{"codec_type":"video"}]}""").RootElement, 1, null));

        Assert.Equal(ToolFailureText.OutputNotPublishable, ToolFailureText.Plain(wrongAudio));
        Assert.Contains("audio", wrongAudio.Message, StringComparison.Ordinal);
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
            ToolFailureText.UnreadableFile, ToolFailureText.UnreadableFileRefusal(4, 65), ToolFailureText.NotReadableYet, ToolFailureText.UnreadableKept,
            ToolFailureText.IncompleteRead, ToolFailureText.TookTooLong, ToolFailureText.TooSlow, ToolFailureText.WriterMissing, ToolFailureText.UnusableTrackData,
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
    public void A_failure_outside_the_tools_is_never_worded_by_its_own_message()
    {
        Assert.Equal(ToolFailureText.UnreadableFile, ToolFailureText.ForFailure(new MediaUnreadableException("[matroska,webm @ 0x1] EBML header parsing failed")));
        Assert.Equal(ToolFailureText.TookTooLong, ToolFailureText.ForFailure(new MediaToolTimeoutException("timed out")));
        Assert.Equal("Weir wrote this.", ToolFailureText.ForFailure(new MkvmergeUnsupportedPlanException("Weir wrote this.")));
        Assert.Equal(ToolFailureText.Generic, ToolFailureText.ForFailure(new IOException(@"The process cannot access the file C:\Media\Film.mkv")));
    }

    [Fact]
    public void A_cause_the_tools_name_has_a_known_reason_and_any_other_exception_has_none()
    {
        Assert.Equal(ToolFailureText.UnreadableFile, ToolFailureText.KnownReason(new MediaUnreadableException("[matroska,webm @ 0x1] EBML header parsing failed")));
        Assert.Equal(ToolFailureText.TookTooLong, ToolFailureText.KnownReason(new MediaToolTimeoutException("timed out")));
        Assert.Equal(ToolFailureText.NoSpace, ToolFailureText.KnownReason(new MediaToolException("ffmpeg: No space left on device") { PlainMessage = ToolFailureText.NoSpace }));
        Assert.Null(ToolFailureText.KnownReason(new MediaToolException("a sentence Weir wrote")));
        Assert.Null(ToolFailureText.KnownReason(new InvalidOperationException("boom")));
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
