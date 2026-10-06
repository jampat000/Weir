using System.Diagnostics;
using System.Text.Json.Nodes;
using Weir.Contract.Tests.Harness.Fakes;

namespace Weir.Contract.Tests.Processing;

/// <summary>Runs the real ffmpeg and ffprobe directly, to generate tiny media files and to look inside the files Weir writes.</summary>
internal static class RealMedia
{
    /// <summary>A two-or-three-second test video (mpeg4) with one mp2 audio track per entry of <paramref name="audioLanguages"/> (tagged with that language unless it is empty), written to <paramref name="path"/>.</summary>
    public static async Task GenerateAsync(string path, int seconds, params string[] audioLanguages)
    {
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"testsrc=duration={seconds}:size=64x64:rate=10" };
        for (var i = 0; i < audioLanguages.Length; i++)
        {
            arguments.AddRange(["-f", "lavfi", "-i", $"sine=frequency={440 * (i + 1)}:duration={seconds}"]);
        }

        arguments.AddRange(["-map", "0:v"]);
        for (var i = 0; i < audioLanguages.Length; i++)
        {
            arguments.AddRange(["-map", $"{i + 1}:a"]);
        }

        arguments.AddRange(["-c:v", "mpeg4", "-c:a", "mp2"]);
        for (var i = 0; i < audioLanguages.Length; i++)
        {
            if (audioLanguages[i].Length > 0)
            {
                arguments.AddRange([$"-metadata:s:a:{i}", $"language={audioLanguages[i]}"]);
            }
        }

        arguments.Add(path);
        await RunAsync(Tool("ffmpeg"), arguments);
    }

    public static async Task<string[]> AudioLanguagesAsync(string path)
    {
        var output = await RunAsync(Tool("ffprobe"), ["-v", "error", "-print_format", "json", "-show_streams", path]);
        return [.. JsonNode.Parse(output)!["streams"]!.AsArray()
            .Where(stream => (string?)stream!["codec_type"] == "audio")
            .Select(stream => (string?)stream!["tags"]?["language"] ?? string.Empty)];
    }

    private static string Tool(string name)
    {
        var folder = RealFfmpeg.Folder ?? throw new InvalidOperationException(RealFfmpeg.MissingReason);
        return new[] { Path.Combine(folder, name + ".exe"), Path.Combine(folder, name) }.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"{name} not found in {folder}");
    }

    private static async Task<string> RunAsync(string program, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"{Path.GetFileName(program)} exited {process.ExitCode}: {await error}");
        return await output;
    }
}
