using System.Text.Json;
using Weir.Core.Json;
using Weir.Core.Rules;

namespace Weir.Core.Media;

/// <summary>Reading a probe document's container family and title, the fields #500's plan check needs off ffprobe's own JSON.</summary>
public static partial class RemuxOutputValidation
{
    private static readonly (string Label, string[] Markers)[] ContainerFamilies =
    [
        ("matroska/webm", ["matroska", "webm"]),
        ("mov/mp4", ["mov", "mp4", "m4a", "3gp", "3g2", "mj2"]),
    ];

    /// <summary>
    /// ffprobe's <c>format.format_name</c>, canonicalized to its muxer family: Matroska and
    /// WebM share a muxer family, as do the MOV/MP4-derived containers, so a WebM output for a Matroska plan is not
    /// a container flip. An unrecognized format name is returned lower-cased, so an exact match still passes and
    /// any other value still fails.
    /// </summary>
    public static string ContainerFamily(string? formatName)
    {
        var lowered = Py.Lower(PyStrings.Strip(formatName ?? string.Empty));
        if (lowered.Length == 0)
        {
            return string.Empty;
        }

        var tokens = lowered.Split(',');
        foreach (var (label, markers) in ContainerFamilies)
        {
            if (markers.Any(tokens.Contains))
            {
                return label;
            }
        }

        return lowered;
    }

    /// <summary><c>format.format_name</c> off a probe document, or null when absent.</summary>
    public static string? FormatName(JsonElement probe)
    {
        if (probe.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var format = Py.Get(probe, "format");
        if (!Py.IsDict(format))
        {
            return null;
        }

        var name = Py.Get(format!.Value, "format_name");
        return Py.Truthy(name) ? Py.Str(name) : null;
    }

    /// <summary><c>format.tags.title</c> off a probe document, or null when absent or blank.</summary>
    public static string? FormatTitle(JsonElement probe)
    {
        if (probe.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var format = Py.Get(probe, "format");
        if (!Py.IsDict(format))
        {
            return null;
        }

        var tags = Py.Get(format!.Value, "tags");
        if (!Py.IsDict(tags))
        {
            return null;
        }

        var title = Py.Get(tags!.Value, "title");
        return Py.Truthy(title) ? Py.Str(title) : null;
    }
}
