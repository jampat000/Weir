using System.Text.Json.Nodes;
using Weir.Contract.FakeTools;

namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>One call the server made to a fake tool.</summary>
/// <param name="Tool">ffprobe or ffmpeg.</param>
/// <param name="Arguments">The arguments, exactly as the server passed them.</param>
/// <param name="Step">For ffmpeg: query, integrity or remux.</param>
/// <param name="File">The base name of the input file, when the call had one.</param>
/// <param name="Attempt">For a remux: how many remuxes of that file name there have been, this one included.</param>
public sealed record ToolCall(string Tool, IReadOnlyList<string> Arguments, string? Step, string? File, int? Attempt)
{
    internal static ToolCall From(JsonObject entry) => new(
        entry[FakeToolProtocol.ToolKey]?.GetValue<string>() ?? string.Empty,
        (entry[FakeToolProtocol.ArgvKey] as JsonArray ?? new JsonArray()).Select(argument => argument?.GetValue<string>() ?? string.Empty).ToList(),
        entry[FakeToolProtocol.StepKey]?.GetValue<string>(),
        entry[FakeToolProtocol.FileKey]?.GetValue<string>(),
        entry[FakeToolProtocol.AttemptKey]?.GetValue<int>());
}
