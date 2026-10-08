using System.Text.Json.Nodes;

namespace Weir.Contract.FakeTools;

/// <summary>ffprobe: answers with the JSON the input file stands for, or fails the way a test told it to.</summary>
internal static class FakeFfprobe
{
    // Weir's own staged outputs carry this in their name; a probe error scripted for the source never applies to them.
    private const string StagedOutputMarker = ".processing.";

    public static int Run(ToolFolder folder, ToolScript script, string[] argv)
    {
        var path = argv.Length > 0 ? argv[^1] : string.Empty;
        folder.LogCall("ffprobe", argv, (FakeToolProtocol.FileKey, Path.GetFileName(path)));
        var rule = script.RuleFor(path);
        if (!File.Exists(path))
        {
            Output.Error($"{path}: No such file or directory");
            return 1;
        }

        if (Number(rule, FakeToolProtocol.ProbeDelaySecondsKey) is > 0 and var delay)
        {
            Thread.Sleep(TimeSpan.FromSeconds(delay));
        }

        if (Text(rule, FakeToolProtocol.ProbeErrorKey) is { } error && !Path.GetFileName(path).Contains(StagedOutputMarker, StringComparison.Ordinal))
        {
            Output.Error(error);
            return 1;
        }

        Output.Out(MediaProbe.For(rule, path).ToJsonString());
        return 0;
    }

    /// <summary>The rule's number for <paramref name="key"/>, or 0 when it is missing.</summary>
    public static double Number(JsonObject rule, string key) =>
        rule[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0;

    /// <summary>The rule's text for <paramref name="key"/>, or null when it is missing or empty.</summary>
    public static string? Text(JsonObject rule, string key) =>
        rule[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
}
