using System.Text.Json;
using Weir.Core.Json;
using Weir.Core.Rules;

namespace Weir.Core.Processing.RemuxPass;

/// <summary>What a pass reads off the probe beyond the plan.</summary>
public static class RemuxPassMedia
{
    /// <summary>The value as text, or empty when it is missing, null, false, zero or empty.</summary>
    public static string TruthyText(JsonElement? value) => RulesJson.Truthy(value) ? RulesJson.Str(value) : string.Empty;

    /// <summary>The value as an integer, or 0 when it is empty or not a number.</summary>
    public static long IntegerOrZero(JsonElement? value) => RulesJson.Truthy(value) && RulesJson.TryInt(value, out var parsed) ? parsed : 0;

    /// <summary>(width, height) of the largest video stream, by <c>width</c>/<c>coded_width</c>.</summary>
    public static (long? Width, long? Height) VideoDimensions(IReadOnlyList<ProbeStreamInfo> videoStreams) =>
        (LargestDimension(videoStreams, "width", "coded_width"), LargestDimension(videoStreams, "height", "coded_height"));

    private static long? LargestDimension(IReadOnlyList<ProbeStreamInfo> streams, params string[] keys)
    {
        long? largest = null;
        foreach (var stream in streams)
        {
            foreach (var key in keys)
            {
                if (!RulesJson.TryInt(stream.Get(key), out var value))
                {
                    continue;
                }

                if (value > 0)
                {
                    largest = largest is null ? value : Math.Max(largest.Value, value);
                    break;
                }
            }
        }

        return largest;
    }

    /// <summary>The longest positive duration among the format and the streams.</summary>
    public static double? ProbeDurationSeconds(ProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var candidates = new List<double>();
        if (probe.Json.ValueKind == JsonValueKind.Object && RulesJson.Get(probe.Json, "format") is { ValueKind: JsonValueKind.Object } format)
        {
            AddFloat(candidates, RulesJson.Get(format, "duration"));
        }

        foreach (var stream in probe.Streams)
        {
            AddFloat(candidates, stream.Get("duration"));
        }

        var valid = candidates.Where(value => value > 0).ToList();
        return valid.Count > 0 ? valid.Max() : null;
    }

    /// <summary>Add the value as a number: 0 when empty, nothing at all when it cannot be read as one.</summary>
    private static void AddFloat(List<double> candidates, JsonElement? value)
    {
        if (!RulesJson.Truthy(value))
        {
            candidates.Add(0);
            return;
        }

        var element = value!.Value;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                candidates.Add(element.GetDouble());
                break;
            case JsonValueKind.True:
                candidates.Add(1);
                break;
            case JsonValueKind.String when RulesJson.TryFloatFromText(element.GetString()!) is { } parsed:
                candidates.Add(parsed);
                break;
        }
    }

    /// <summary>The video bit depth: <c>bits_per_raw_sample</c>, else read from the pixel format, else null.</summary>
    public static long? VideoBitDepth(ProbeStreamInfo stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var raw = stream.Get("bits_per_raw_sample");
        if (raw is { } element && element.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            var text = WireStrings.Strip(RulesJson.Str(raw));
            if (text.Length > 0 && text.All(char.IsAsciiDigit) && RulesJson.TryInt(raw, out var bits) && bits > 0)
            {
                return bits;
            }
        }

        var pixelFormat = TruthyText(stream.Get("pix_fmt")).ToLowerInvariant();
        if (pixelFormat.Length == 0)
        {
            return null;
        }

        foreach (var depth in new[] { 12, 10 })
        {
            if (pixelFormat.Contains($"{depth}le", StringComparison.Ordinal) || pixelFormat.Contains($"{depth}be", StringComparison.Ordinal) ||
                pixelFormat.EndsWith($"p{depth}", StringComparison.Ordinal))
            {
                return depth;
            }
        }

        return 8;
    }

    /// <summary>
    /// An unchanged file described without applying any rules. Observability only; pass-through
    /// never runs ffmpeg.
    /// </summary>
    public static RemuxPlan PassThroughPlan(IReadOnlyList<ProbeStreamInfo> video, IReadOnlyList<ProbeStreamInfo> audio, IReadOnlyList<ProbeStreamInfo> subtitles)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(subtitles);
        return new RemuxPlan
        {
            VideoIndices = [.. video.Select(stream => (int)IntegerOrZero(stream.Get("index")))],
            Audio = [.. audio.Select(stream => new PlannedTrack
            {
                InputIndex = (int)IntegerOrZero(stream.Get("index")),
                LangLabel = Language(stream),
                Forced = Flag(stream, "forced"),
                Default = Flag(stream, "default"),
                Channels = (int)IntegerOrZero(stream.Get("channels")),
                Bitrate = IntegerOrZero(stream.Get("bit_rate")),
                CodecName = TruthyText(stream.Get("codec_name")),
                Kind = TrackKind.Audio,
            })],
            Subtitles = [.. subtitles.Select(stream => new PlannedTrack
            {
                InputIndex = (int)IntegerOrZero(stream.Get("index")),
                LangLabel = Language(stream),
                Forced = Flag(stream, "forced"),
                Default = Flag(stream, "default"),
                Kind = TrackKind.Subtitle,
            })],
            AudioSelectionNotes = ["The operator chose Pass through unchanged, so every stream was preserved."],
        };
    }

    private static string Language(ProbeStreamInfo stream)
    {
        if (stream.Get("tags") is { ValueKind: JsonValueKind.Object } tags && RulesJson.Get(tags, "language") is { } language && RulesJson.Truthy(language))
        {
            return RulesJson.Str(language);
        }

        return "und";
    }

    private static bool Flag(ProbeStreamInfo stream, string name) =>
        stream.Get("disposition") is { ValueKind: JsonValueKind.Object } disposition && RulesJson.Truthy(RulesJson.Get(disposition, name));
}
