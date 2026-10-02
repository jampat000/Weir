using Weir.Core.Processing;

namespace Weir.Core.Tests.Processing;

/// <summary>What each file status means, which is what a list of files sorted by status goes by.</summary>
public sealed class ProcessingFileMeaningsTests
{
    [Fact]
    public void Every_status_has_a_meaning()
    {
        Assert.All(ProcessingFileStatuses.All, status => Assert.True(ProcessingFileMeanings.OfStatus.ContainsKey(status), status));
        Assert.Equal(ProcessingFileStatuses.All.Count, ProcessingFileMeanings.OfStatus.Count);
    }

    [Theory]
    [InlineData(ProcessingFileStatuses.Processed, ProcessingFileMeaning.Done)]
    [InlineData(ProcessingFileStatuses.Unprocessed, ProcessingFileMeaning.Todo)]
    [InlineData(ProcessingFileStatuses.OutOfSchedule, ProcessingFileMeaning.Todo)]
    [InlineData(ProcessingFileStatuses.Processing, ProcessingFileMeaning.Doing)]
    [InlineData(ProcessingFileStatuses.OnHold, ProcessingFileMeaning.Attention)]
    [InlineData(ProcessingFileStatuses.BlockedUpstream, ProcessingFileMeaning.Attention)]
    [InlineData(ProcessingFileStatuses.PassedThrough, ProcessingFileMeaning.Attention)]
    [InlineData(ProcessingFileStatuses.Rejected, ProcessingFileMeaning.Attention)]
    [InlineData(ProcessingFileStatuses.ProcessingFailed, ProcessingFileMeaning.Broken)]
    [InlineData(ProcessingFileStatuses.Skipped, ProcessingFileMeaning.Idle)]
    [InlineData(ProcessingFileStatuses.Disabled, ProcessingFileMeaning.Idle)]
    [InlineData(ProcessingFileStatuses.Cancelled, ProcessingFileMeaning.Idle)]
    public void A_status_means_what_the_status_words_on_screen_say(string status, ProcessingFileMeaning meaning)
    {
        Assert.Equal(meaning, ProcessingFileMeanings.OfStatus[status]);
    }

    [Fact]
    public void Meanings_rank_done_then_todo_then_doing_then_attention_then_broken_then_idle()
    {
        var ranked = new[] { "skipped", "processing_failed", "processing", "processed", "rejected", "unprocessed" }
            .OrderBy(ProcessingFileMeanings.RankOf)
            .ToList();

        Assert.Equal(["processed", "unprocessed", "processing", "rejected", "processing_failed", "skipped"], ranked);
    }

    [Fact]
    public void A_status_with_no_meaning_ranks_after_every_meaning()
    {
        Assert.True(ProcessingFileMeanings.RankOf("something_new") > ProcessingFileMeanings.RankOf(ProcessingFileStatuses.Skipped));
    }

    [Theory]
    [InlineData("file", ProcessingFileSort.File)]
    [InlineData("status", ProcessingFileSort.Status)]
    [InlineData("when", ProcessingFileSort.When)]
    public void A_sort_name_on_the_wire_is_the_sort_it_stands_for(string name, ProcessingFileSort expected)
    {
        Assert.True(ProcessingFileSorts.TryParse(name, out var sort));
        Assert.Equal(expected, sort);
        Assert.Equal(name, ProcessingFileSorts.NameOf(sort));
    }

    [Theory]
    [InlineData("last_seen")]
    [InlineData("When")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_that_is_not_one_of_the_three_sorts_is_not_a_sort(string? name)
    {
        Assert.False(ProcessingFileSorts.TryParse(name, out _));
    }
}
