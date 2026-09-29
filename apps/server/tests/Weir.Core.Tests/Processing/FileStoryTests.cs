using Weir.Core.Json;
using Weir.Core.Processing;
using Weir.Core.Processing.RemuxPass;

namespace Weir.Core.Tests.Processing;

/// <summary>The file story (#468): narrate a stored pass record in plain language, never throwing on a
/// partial or malformed one.</summary>
public sealed class FileStoryTests
{
    [Fact]
    public void An_empty_record_with_no_library_name_produces_no_steps() => Assert.Empty(FileStory.NarratePass(new WireObject(), string.Empty));

    [Fact]
    public void An_empty_record_still_reports_where_the_file_was_picked_up_when_a_library_name_is_known()
    {
        // _picked_up runs regardless of "ok" and needs only a library name or a media scope to say something.
        var step = Assert.Single(FileStory.NarratePass(new WireObject(), "Movies"));
        Assert.Equal("Picked up", step.Heading);
        Assert.Equal("Weir took this file in the Movies workflow.", step.Sentence);
    }

    [Fact]
    public void A_successful_pass_reports_picked_up_and_worked_steps()
    {
        var detail = new WireObject()
            .Set("ok", true)
            .Set("media_scope", "movie")
            .Set("elapsed_seconds", 12.0)
            .Set("source_size_bytes", 2000.0)
            .Set("output_size_bytes", 1000.0);

        var steps = FileStory.NarratePass(detail, "Movies");

        var pickedUp = Assert.Single(steps, s => s.Heading == "Picked up");
        Assert.Contains("Movies", pickedUp.Sentence, StringComparison.Ordinal);
        Assert.Contains("film", pickedUp.Sentence, StringComparison.Ordinal);

        var worked = Assert.Single(steps, s => s.Heading == "Worked");
        Assert.Equal(StoryTone.Good, worked.Tone);
        Assert.Contains("saving", worked.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_pass_reports_the_reason_with_a_bad_tone()
    {
        // No library name: _picked_up has nothing to say, so the failure is the only step.
        var detail = new WireObject().Set("ok", false).Set("reason", "No retainable audio track.");

        var steps = FileStory.NarratePass(detail, string.Empty);

        var failed = Assert.Single(steps);
        Assert.Equal("Could not finish", failed.Heading);
        Assert.Equal("No retainable audio track.", failed.Sentence);
        Assert.Equal(StoryTone.Bad, failed.Tone);
    }

    [Fact]
    public void A_failed_pass_with_no_reason_gets_a_generic_sentence_rather_than_a_blank_one()
    {
        var steps = FileStory.NarratePass(new WireObject().Set("ok", false), string.Empty);

        var failed = Assert.Single(steps);
        Assert.Equal("Weir could not process this file, and the record does not say why.", failed.Sentence);
    }

    [Fact]
    public void A_rejection_no_media_manager_is_involved_in_reads_as_rejected_with_its_reason_and_what_became_of_the_file()
    {
        var detail = new WireObject()
            .Set("ok", false)
            .Set(RejectionResultKeys.WithoutManager, true)
            .Set("reason", "Rejected: none of its audio tracks are in English, so there would be nothing to keep.")
            .Set("rejected_cleanup_detail", "The file was left where it is.");

        var rejected = Assert.Single(FileStory.NarratePass(detail, string.Empty));

        Assert.Equal("Rejected", rejected.Heading);
        Assert.Equal("None of its audio tracks are in English, so there would be nothing to keep. The file was left where it is.", rejected.Sentence);
        Assert.Equal(StoryTone.Warn, rejected.Tone);
    }

    [Fact]
    public void A_rejection_whose_reason_is_missing_still_says_it_was_rejected()
    {
        var detail = new WireObject().Set("ok", false).Set(RejectionResultKeys.WithoutManager, true);

        var rejected = Assert.Single(FileStory.NarratePass(detail, string.Empty));

        Assert.Equal("Rejected", rejected.Heading);
        Assert.Equal("Weir rejected this file.", rejected.Sentence);
    }

    [Fact]
    public void A_pass_that_failed_while_a_media_manager_is_asked_to_replace_the_release_still_reads_as_could_not_finish()
    {
        var detail = new WireObject()
            .Set("ok", false)
            .Set("reject_queued", true)
            .Set("reason", "No retainable audio track.");

        var failed = Assert.Single(FileStory.NarratePass(detail, string.Empty));

        Assert.Equal("Could not finish", failed.Heading);
    }

    [Fact]
    public void A_pass_that_needed_no_changes_is_reported_as_good_news()
    {
        var detail = new WireObject().Set("ok", true).Set("remux_required", false);

        var planned = Assert.Single(FileStory.NarratePass(detail, "Movies"), s => s.Heading == "Planned");
        Assert.Equal(StoryTone.Good, planned.Tone);
        Assert.Contains("nothing to change", planned.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void Stream_copy_is_only_claimed_when_the_argv_proves_it()
    {
        var detailWithCopy = new WireObject()
            .Set("ok", true)
            .Set("remux_required", true)
            .Set("removed_audio", new WireArray([WireValue.Of("commentary")]))
            .Set("ffmpeg_argv", new WireArray([WireValue.Of("-c:v"), WireValue.Of("copy")]));
        var planned = Assert.Single(FileStory.NarratePass(detailWithCopy, "Movies"), s => s.Heading == "Planned");
        Assert.Contains("not re-encoded", planned.Sentence, StringComparison.Ordinal);

        var detailWithoutArgv = new WireObject()
            .Set("ok", true)
            .Set("remux_required", true)
            .Set("removed_audio", new WireArray([WireValue.Of("commentary")]));
        var plannedNoProof = Assert.Single(FileStory.NarratePass(detailWithoutArgv, "Movies"), s => s.Heading == "Planned");
        Assert.DoesNotContain("not re-encoded", plannedNoProof.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_of_a_linked_workflow_is_said_to_be_handed_back_for_the_media_manager_to_import()
    {
        var detail = new WireObject().Set("ok", true).Set("output_file", "/ready/movies/Film.mkv");

        var handedBack = Assert.Single(FileStory.NarratePass(detail, "Movies", HandBackTarget.MediaManager), s => s.Heading == "Handed back");

        Assert.Equal("The result was written to /ready/movies/Film.mkv for your media manager to import.", handedBack.Sentence);
    }

    [Fact]
    public void A_file_of_a_workflow_with_no_media_manager_is_said_to_be_in_the_output_folder()
    {
        var detail = new WireObject().Set("ok", true).Set("output_file", "/ready/movies/Film.mkv");

        var steps = FileStory.NarratePass(detail, "Movies", HandBackTarget.OutputFolder);

        var cleaned = Assert.Single(steps, s => s.Heading == "Cleaned copy");
        Assert.Equal("The cleaned copy is in the output folder, at /ready/movies/Film.mkv.", cleaned.Sentence);
        Assert.DoesNotContain(steps, s => s.Sentence.Contains("media manager", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unparseable_field_is_skipped_rather_than_throwing()
    {
        // narrate_pass never raises on a partial record: garbage in a numeric-looking field is ignored.
        var detail = new WireObject().Set("ok", true).Set("elapsed_seconds", "not-a-number");
        var steps = FileStory.NarratePass(detail, "Movies");
        Assert.DoesNotContain(steps, s => s.Heading == "Worked");
    }
}
