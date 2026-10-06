using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Weir.Contract.FakeTools;

/// <summary>
/// Real ffmpeg's <c>-disposition:{v|a|s}:{n} value</c> edits stream n's flags (counting among streams of that type only).
/// Weir uses the additive <c>+flag</c>/<c>-flag</c> form, which changes only the named flags and leaves every other flag as the
/// source stream had it. The output is validated against the dispositions Weir asked for, so the fake must honour them.
/// </summary>
internal static partial class Dispositions
{
    private const string Prefix = "-disposition:";

    private static readonly Dictionary<string, string> CodecTypes = new() { ["v"] = "video", ["a"] = "audio", ["s"] = "subtitle" };

    public static void Apply(List<JsonObject> streams, string[] argv)
    {
        for (var i = 0; i < argv.Length - 1; i++)
        {
            if (!argv[i].StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var spec = argv[i][Prefix.Length..].Split(':', 2);
            if (spec.Length != 2 || !CodecTypes.TryGetValue(spec[0], out var codecType) || !IsDigits(spec[1]))
            {
                continue;
            }

            var candidates = streams.Where(stream => stream["codec_type"]?.GetValue<string>() == codecType).ToList();
            if (!int.TryParse(spec[1], out var position) || position >= candidates.Count)
            {
                continue;
            }

            var stream = candidates[position];
            stream["disposition"] = Changed(stream["disposition"] as JsonObject ?? new JsonObject(), argv[i + 1]);
        }
    }

    private static JsonObject Changed(JsonObject current, string value)
    {
        var disposition = (JsonObject)current.DeepClone();
        if (value != "0" && (value.Contains('+', StringComparison.Ordinal) || value.Contains('-', StringComparison.Ordinal)))
        {
            foreach (Match flag in FlagToken().Matches(value))
            {
                disposition[flag.Groups[2].Value] = flag.Groups[1].Value == "+" ? 1 : 0;
            }

            return disposition;
        }

        // The older flat forms: "0" clears every flag; anything else replaces them with the named, "+"-joined flags.
        foreach (var name in disposition.Select(pair => pair.Key).ToList())
        {
            disposition[name] = 0;
        }

        if (value != "0")
        {
            foreach (var flag in value.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                disposition[flag] = 1;
            }
        }

        return disposition;
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    [GeneratedRegex("([+-])([A-Za-z_]+)")]
    private static partial Regex FlagToken();
}
