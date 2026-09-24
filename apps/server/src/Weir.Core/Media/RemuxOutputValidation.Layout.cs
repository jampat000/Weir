using System.Text.Json;
using Weir.Core.Rules;

namespace Weir.Core.Media;

/// <summary>One track the plan expects to find at a given output position.</summary>
/// <param name="CodecType">"video", "audio" or "subtitle".</param>
/// <param name="Default">The disposition the plan sets, or null when the plan does not care (video).</param>
/// <param name="Forced">The disposition the plan sets, or null when the plan does not care (video).</param>
/// <param name="Language">
/// The normalized language the plan sets, or null when the plan does not tag one (unknown source language, or
/// <see cref="MetadataRules.RemoveLanguageTags"/> strips it on purpose).
/// </param>
public sealed record PlannedOutputTrack(string CodecType, bool? Default, bool? Forced, string? Language);

/// <summary>One track ffprobe actually found in the output, at its position in the file.</summary>
public sealed record ActualOutputTrack(string CodecType, bool Default, bool Forced, string? Language);

/// <summary>
/// The plan's expected track layout and ffprobe's actual one, both in output position order, for
/// <see cref="RemuxOutputValidation.ValidateAgainstPlan"/> to compare position by position.
/// </summary>
public static partial class RemuxOutputValidation
{
    /// <summary>
    /// The order <see cref="FfmpegCommands.BuildRemuxArgv"/> maps streams in: every kept video, then the one kept
    /// audio track, then the kept subtitles. Output position <c>i</c> must be this list's entry <c>i</c>.
    /// </summary>
    public static IReadOnlyList<PlannedOutputTrack> ExpectedLayout(RemuxPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var languageTagsRemoved = plan.Metadata.RemoveLanguageTags;
        var result = new List<PlannedOutputTrack>(plan.VideoIndices.Count + plan.Audio.Count + plan.Subtitles.Count);
        for (var i = 0; i < plan.VideoIndices.Count; i++)
        {
            result.Add(new PlannedOutputTrack("video", null, null, null));
        }

        foreach (var track in plan.Audio)
        {
            result.Add(new PlannedOutputTrack("audio", track.Default, track.Forced, PlannedLanguage(track, languageTagsRemoved)));
        }

        foreach (var track in plan.Subtitles)
        {
            result.Add(new PlannedOutputTrack("subtitle", track.Default, track.Forced, PlannedLanguage(track, languageTagsRemoved)));
        }

        return result;
    }

    private static string? PlannedLanguage(PlannedTrack track, bool languageTagsRemoved) =>
        languageTagsRemoved || track.LangLabel.Length == 0 ? null : track.LangLabel;

    /// <summary>The tracks ffprobe actually found in a probe document, in output order (ffprobe already lists streams in that order).</summary>
    public static IReadOnlyList<ActualOutputTrack> ActualLayout(JsonElement probe)
    {
        if (probe.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var streams = Py.Get(probe, "streams");
        if (!Py.IsList(streams))
        {
            return [];
        }

        var result = new List<ActualOutputTrack>();
        foreach (var stream in streams!.Value.EnumerateArray())
        {
            if (stream.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var codecType = Py.IsStr(Py.Get(stream, "codec_type")) ? Py.Lower(Py.Get(stream, "codec_type")!.Value.GetString()!) : string.Empty;

            // #547: an attachment stream (a font for ASS/SSA, say) is mapped through unchanged when the container
            // supports it, but it is not part of the plan's video/audio/subtitle layout — the plan has no
            // PlannedOutputTrack for it and never will, so it never belongs in this position-by-position comparison.
            if (codecType == "attachment")
            {
                continue;
            }

            var disposition = Py.Get(stream, "disposition");
            var tags = Py.Get(stream, "tags");
            string? language = null;
            if (Py.IsDict(tags))
            {
                var raw = Py.Get(tags!.Value, "language");
                if (Py.Truthy(raw) && Py.IsStr(raw))
                {
                    language = raw!.Value.GetString();
                }
            }

            result.Add(new ActualOutputTrack(codecType, DispositionFlag(disposition, "default"), DispositionFlag(disposition, "forced"), language));
        }

        return result;
    }

    private static bool DispositionFlag(JsonElement? disposition, string name) =>
        Py.IsDict(disposition) && Py.TryInt(Py.Get(disposition!.Value, name), out var flag) && flag != 0;
}
