using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Weir.Core.Json;
using Weir.Core.Rules;

namespace Weir.Core.Media;

/// <summary>#500: the expected duration to validate a staged output against, from the source's kept streams.</summary>
public static partial class RemuxOutputValidation
{
    [GeneratedRegex(@"^(\d+):([0-5]?\d):([0-5]?\d(?:\.\d+)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex TagsDurationRegex();

    /// <summary>
    /// The max duration reported by any stream the plan keeps (its own <c>duration</c>, or a Matroska
    /// <c>tags.DURATION</c>), ignoring streams the plan drops. Null when none of the kept streams reports one, so
    /// the caller should fall back to measuring directly.
    /// </summary>
    public static double? ExpectedDurationFromKeptStreams(JsonElement sourceProbe, RemuxPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var kept = new HashSet<int>(plan.VideoIndices);
        foreach (var track in plan.Audio)
        {
            kept.Add(track.InputIndex);
        }

        foreach (var track in plan.Subtitles)
        {
            kept.Add(track.InputIndex);
        }

        double? best = null;
        if (sourceProbe.ValueKind == JsonValueKind.Object && Py.Get(sourceProbe, "streams") is { } streams && Py.IsList(streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!Py.TryInt(Py.Get(stream, "index"), out var index) || !kept.Contains((int)index))
                {
                    continue;
                }

                var candidate = StreamDurationSeconds(stream);
                if (candidate is { } value && value > 0 && (best is null || value > best.Value))
                {
                    best = value;
                }
            }
        }

        return best;
    }

    private static double? StreamDurationSeconds(JsonElement stream)
    {
        var duration = Py.Get(stream, "duration");
        if (Py.Truthy(duration))
        {
            var parsed = duration!.Value.ValueKind switch
            {
                JsonValueKind.Number => duration.Value.GetDouble(),
                JsonValueKind.String => Py.TryFloatFromText(duration.Value.GetString()!),
                _ => (double?)null,
            };
            if (parsed is { } value && value > 0)
            {
                return value;
            }
        }

        var tags = Py.Get(stream, "tags");
        if (Py.IsDict(tags))
        {
            var raw = Py.Get(tags!.Value, "DURATION");
            if (Py.Truthy(raw) && Py.IsStr(raw) && ParseTagsDuration(raw!.Value.GetString()) is { } seconds)
            {
                return seconds;
            }
        }

        return null;
    }

    /// <summary>Parses a Matroska-style <c>HH:MM:SS.sssssssss</c> duration tag into seconds, or null when it does not match.</summary>
    public static double? ParseTagsDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = TagsDurationRegex().Match(PyStrings.Strip(text));
        if (!match.Success)
        {
            return null;
        }

        var hours = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var total = (hours * 3600) + (minutes * 60) + seconds;
        return total > 0 ? total : null;
    }
}
