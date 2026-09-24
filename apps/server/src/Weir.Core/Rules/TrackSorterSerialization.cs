using System.Text;
using System.Text.Json;
using Weir.Core.Json;

namespace Weir.Core.Rules;

/// <summary>Reading, validating and writing the sorter list as stored JSON, and the sentence for the selection notes.</summary>
public static partial class TrackSorters
{
    /// <summary>Read a stored list. Anything unusable yields the seeded default rather than none.</summary>
    public static IReadOnlyList<TrackSorter> Parse(string? raw)
    {
        var text = WireStrings.Strip(raw ?? string.Empty);
        if (text.Length == 0)
        {
            return [.. DefaultAudioSorters];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return [.. DefaultAudioSorters];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [.. DefaultAudioSorters];
            }

            var result = new List<TrackSorter>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var sorter = ReadEntry(item);
                if (!Fields.Contains(sorter.Field))
                {
                    continue;
                }

                result.Add(sorter);
            }

            return result.Count > 0 ? result : [.. DefaultAudioSorters];
        }
    }

    private static TrackSorter ReadEntry(JsonElement item)
    {
        var field = RulesJson.Lower(WireStrings.Strip(RulesJson.StrOr(RulesJson.Get(item, "field"), string.Empty)));
        var value = RulesJson.Get(item, "value");
        var keptValue = RulesJson.IsStr(value) && WireStrings.Strip(value!.Value.GetString()!).Length > 0 ? value.Value.GetString() : null;
        return new TrackSorter(field, keptValue, RulesJson.Truthy(RulesJson.Get(item, "reversed")));
    }

    /// <summary>Compact, ASCII-escaped JSON, byte-identical to the sorter JSON already stored in rule sets.</summary>
    public static string Dump(IEnumerable<TrackSorter> sorters)
    {
        ArgumentNullException.ThrowIfNull(sorters);
        var builder = new StringBuilder("[");
        var first = true;
        foreach (var sorter in sorters)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append("{\"field\":");
            WireJsonWriter.WriteString(builder, sorter.Field, ensureAscii: true);
            builder.Append(",\"value\":");
            if (sorter.Value is null)
            {
                builder.Append("null");
            }
            else
            {
                WireJsonWriter.WriteString(builder, sorter.Value, ensureAscii: true);
            }

            builder.Append(",\"reversed\":").Append(sorter.Reversed ? "true" : "false").Append('}');
        }

        return builder.Append(']').ToString();
    }

    /// <summary>Validate a submitted list, refusing rather than silently dropping entries.</summary>
    public static string Validate(string? raw)
    {
        var text = WireStrings.Strip(raw ?? string.Empty);
        if (text.Length == 0)
        {
            return Dump(DefaultAudioSorters);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new TrackSorterException("The track sorter list is not valid JSON.", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new TrackSorterException("The track sorter list must be a list.");
            }

            var result = new List<TrackSorter>();
            var position = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                position++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new TrackSorterException($"Sorter {position} is not an object.");
                }

                var sorter = ReadEntry(item);
                if (!Fields.Contains(sorter.Field))
                {
                    throw new TrackSorterException(
                        $"Sorter {position} uses an unknown field {RulesJson.Repr(sorter.Field)}. Known fields: {string.Join(", ", Fields)}.");
                }

                result.Add(sorter);
            }

            return Dump(result);
        }
    }

    /// <summary>The preset for a policy name, or the seeded default.</summary>
    public static IReadOnlyList<TrackSorter> Preset(string? name) =>
        Presets.TryGetValue(RulesJson.Lower(WireStrings.Strip(name ?? string.Empty)), out var preset) ? [.. preset] : [.. DefaultAudioSorters];

    /// <summary>A sentence for the selection notes.</summary>
    public static string Describe(IReadOnlyCollection<TrackSorter> sorters)
    {
        ArgumentNullException.ThrowIfNull(sorters);
        if (sorters.Count == 0)
        {
            return "no ordering configured, so tracks were taken in file order";
        }

        return string.Join(", then ", sorters.Select(s => s.Describe()));
    }
}
