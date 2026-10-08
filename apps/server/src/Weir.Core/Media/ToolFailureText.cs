using Weir.Core.Json;
using Weir.Core.Observability;
using Weir.Core.Rules;

namespace Weir.Core.Media;

/// <summary>
/// The sentences a person (or a media manager) reads when ffprobe, ffmpeg or mkvmerge fail. A tool's own text carries file
/// paths and memory addresses, so it stays in technical detail; this is the one mapping from a tool failure to plain words.
/// </summary>
public static class ToolFailureText
{
    /// <summary>A file the probe could not parse, or that has no streams at all.</summary>
    public const string UnreadableFile = "Weir couldn't read this file: it isn't a video Weir recognises, or it is damaged.";

    /// <summary>The sentence a pass records when it refuses an unreadable file, which it does before writing anything.</summary>
    public const string UnreadableFileRefusal = UnreadableFile + " It was refused before any output was written.";

    /// <summary>A file that reads in part but not from start to finish.</summary>
    public const string IncompleteRead =
        "Weir could not read this media file from start to finish. It may still be downloading or may be incomplete, so Weir will wait.";

    public const string TookTooLong =
        "Weir's media tools took too long on this file, so Weir stopped them. Try the file again; if it keeps happening, the file may be damaged.";

    public const string NoSpace = "The drive Weir was writing to ran out of space. Free some space, then try the file again.";

    public const string NotAllowed =
        "Weir wasn't allowed to open or write a file it needed. Check that Weir can read the watched folder and write to its work and output folders.";

    public const string MissingOrEmpty = "Weir found no data in this file: it is missing or empty.";

    public const string OutputNotPublishable =
        "Weir wrote a cleaned copy, but its checks found a problem with it, so the copy was not published. The original is untouched.";

    public const string Generic =
        "Weir's media tools couldn't finish this file. Try it again; if it keeps happening, the file may be damaged.";

    /// <summary>
    /// The words for <paramref name="exception"/>: the sentence the failure carries for a person, else the one its type calls for,
    /// else its own message, which is then a sentence Weir wrote.
    /// </summary>
    public static string Plain(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            MediaToolException { PlainMessage: { Length: > 0 } plain } => plain,
            MediaUnreadableException => UnreadableFile,
            MediaToolTimeoutException => TookTooLong,
            _ => exception.Message,
        };
    }

    /// <summary>The failure's own message for technical detail: what the tool said, with secrets redacted and capped at 1000 characters.</summary>
    public static string Technical(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return WireStrings.Slice(Diagnostics.SanitizeText("technical_detail", exception.Message), 1000);
    }

    /// <summary>The words for a tool's own failure text (stderr, a path, an address).</summary>
    public static string ForToolText(string toolText)
    {
        ArgumentNullException.ThrowIfNull(toolText);
        var lowered = RulesJson.Lower(toolText);
        if (ProbeOutput.IsUnreadableMedia(lowered))
        {
            return UnreadableFile;
        }

        if (lowered.Contains("timed out", StringComparison.Ordinal))
        {
            return TookTooLong;
        }

        if (lowered.Contains("no space left", StringComparison.Ordinal) || lowered.Contains("not enough space", StringComparison.Ordinal))
        {
            return NoSpace;
        }

        if (lowered.Contains("permission denied", StringComparison.Ordinal) || lowered.Contains("access is denied", StringComparison.Ordinal))
        {
            return NotAllowed;
        }

        return Generic;
    }
}
