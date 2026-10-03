using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Weir.Contract.FakeTools;

/// <summary>What ffprobe reports for a file: the answer the file carries, else the rule's, else a plain video plus English audio.</summary>
internal static class MediaProbe
{
    public static JsonObject For(JsonObject rule, string path)
    {
        if (Embedded(path) is { } embedded)
        {
            return embedded;
        }

        return rule[FakeToolProtocol.ProbeKey] as JsonObject ?? Default();
    }

    public static JsonObject Default() => new()
    {
        ["streams"] = new JsonArray(
            new JsonObject { ["index"] = 0, ["codec_type"] = "video", ["codec_name"] = "h264", ["width"] = 1920, ["height"] = 1080 },
            new JsonObject
            {
                ["index"] = 1,
                ["codec_type"] = "audio",
                ["codec_name"] = "aac",
                ["channels"] = 2,
                ["tags"] = new JsonObject { ["language"] = "eng" },
            }),
        ["format"] = new JsonObject { ["duration"] = "60.0" },
    };

    private static JsonObject? Embedded(string path)
    {
        try
        {
            var magic = Encoding.ASCII.GetBytes(FakeToolProtocol.MediaMagic);
            using var file = File.OpenRead(path);
            var head = new byte[magic.Length];
            if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length || !head.AsSpan().SequenceEqual(magic))
            {
                return null;
            }

            return JsonNode.Parse(file) as JsonObject;
        }
        catch (Exception problem) when (problem is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
