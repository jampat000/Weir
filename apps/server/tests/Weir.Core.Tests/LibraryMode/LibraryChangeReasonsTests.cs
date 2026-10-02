using Weir.Core.LibraryMode;

namespace Weir.Core.Tests.LibraryMode;

public sealed class LibraryChangeReasonsTests
{
    private const long Size = 1_000;
    private const long Modified = 1_700_000_000;

    private static LibraryChangeReasons.Before Known(
        LibraryFileClassification classification = LibraryFileClassification.WouldChange, string? reason = null, long size = Size, long modified = Modified) =>
        new(size, modified, classification, reason);

    private static string? Decide(
        LibraryChangeReasons.Before? before, bool hadEarlierScan = true, long size = Size, long modified = Modified, long? cleanedAt = null,
        LibraryFileClassification now = LibraryFileClassification.WouldChange) =>
        LibraryChangeReasons.Decide(now, hadEarlierScan, before, size, modified, cleanedAt);

    [Fact]
    public void A_file_that_was_not_there_at_the_last_check_is_new()
    {
        Assert.Equal(LibraryChangeReasons.New, Decide(before: null));
    }

    [Fact]
    public void The_first_scan_of_a_library_gives_no_reason_because_there_is_nothing_to_compare_with()
    {
        Assert.Null(Decide(before: null, hadEarlierScan: false));
    }

    [Theory]
    [InlineData(LibraryFileClassification.Matches)]
    [InlineData(LibraryFileClassification.CannotProcess)]
    public void A_file_that_does_not_need_cleaning_has_no_reason(LibraryFileClassification now)
    {
        Assert.Null(Decide(before: null, now: now));
    }

    [Fact]
    public void A_file_with_another_size_or_modified_time_has_been_replaced()
    {
        Assert.Equal(LibraryChangeReasons.Replaced, Decide(Known(), size: Size + 1));
        Assert.Equal(LibraryChangeReasons.Replaced, Decide(Known(), modified: Modified + 60));
    }

    [Fact]
    public void A_replaced_file_that_matched_before_is_replaced_not_rules_changed()
    {
        Assert.Equal(LibraryChangeReasons.Replaced, Decide(Known(LibraryFileClassification.Matches), size: Size + 1));
    }

    [Fact]
    public void The_same_file_that_matched_before_and_no_longer_does_means_the_rules_changed()
    {
        Assert.Equal(LibraryChangeReasons.RulesChanged, Decide(Known(LibraryFileClassification.Matches)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(LibraryChangeReasons.New)]
    [InlineData(LibraryChangeReasons.Replaced)]
    public void The_same_unchanged_file_that_still_needs_cleaning_keeps_its_reason(string? reason)
    {
        Assert.Equal(reason, Decide(Known(reason: reason)));
    }

    [Fact]
    public void A_file_Weir_cleaned_that_no_longer_matches_means_the_rules_changed_even_though_the_clean_changed_the_file()
    {
        // The clean rewrote the file, so its size and time differ from what the last scan saw, but it is no newer than the clean.
        Assert.Equal(
            LibraryChangeReasons.RulesChanged,
            Decide(Known(), size: Size - 400, modified: Modified + 5, cleanedAt: Modified + 10));
    }

    [Fact]
    public void A_cleaned_file_that_changed_after_the_clean_has_been_replaced()
    {
        Assert.Equal(LibraryChangeReasons.Replaced, Decide(Known(), modified: Modified + 100, cleanedAt: Modified + 10));
    }

    [Fact]
    public void A_cleaned_file_that_has_gone_and_come_back_is_new()
    {
        Assert.Equal(LibraryChangeReasons.New, Decide(before: null, cleanedAt: Modified));
    }
}
