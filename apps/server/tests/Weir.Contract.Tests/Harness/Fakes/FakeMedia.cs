using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Weir.Contract.FakeTools;

namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>Files the fake ffprobe/ffmpeg understand: the content carries its own ffprobe answer.</summary>
public static class FakeMedia
{
    /// <summary>File content that the fake ffprobe reports as <paramref name="probe"/>. Padding gives it a plausible size.</summary>
    public static byte[] Bytes(JsonObject probe, int padding = 1024) =>
        Encoding.UTF8.GetBytes(FakeToolProtocol.MediaMagic + probe.ToJsonString() + new string(' ', padding));

    /// <summary>
    /// An ffprobe answer with the given tracks, indexed in order: video, audio, subtitles. Audio languages default to one English
    /// track; pass an empty array for none. Every stream carries its own duration (a string, as real ffprobe reports it), because
    /// staged-output validation expects the kept streams' own duration and a fake, non-playable file cannot be measured.
    /// </summary>
    public static JsonObject Probe(
        int video = 1,
        string[]? audioLanguages = null,
        string[]? subtitleLanguages = null,
        double durationSeconds = 60.0)
    {
        var duration = durationSeconds.ToString("F6", CultureInfo.InvariantCulture);
        var streams = new JsonArray();
        for (var i = 0; i < video; i++)
        {
            streams.Add(new JsonObject
            {
                ["index"] = streams.Count,
                ["codec_type"] = "video",
                ["codec_name"] = "h264",
                ["width"] = 1920,
                ["height"] = 1080,
                ["duration"] = duration,
            });
        }

        foreach (var language in audioLanguages ?? ["eng"])
        {
            streams.Add(new JsonObject
            {
                ["index"] = streams.Count,
                ["codec_type"] = "audio",
                ["codec_name"] = "aac",
                ["channels"] = 2,
                ["duration"] = duration,
                ["tags"] = new JsonObject { ["language"] = language },
            });
        }

        foreach (var language in subtitleLanguages ?? [])
        {
            streams.Add(new JsonObject
            {
                ["index"] = streams.Count,
                ["codec_type"] = "subtitle",
                ["codec_name"] = "subrip",
                ["duration"] = duration,
                ["tags"] = new JsonObject { ["language"] = language },
            });
        }

        return new JsonObject
        {
            ["streams"] = streams,
            ["format"] = new JsonObject { ["duration"] = durationSeconds.ToString("F1", CultureInfo.InvariantCulture) },
        };
    }
}
