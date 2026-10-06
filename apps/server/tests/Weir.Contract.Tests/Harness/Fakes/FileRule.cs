using System.Text.Json.Nodes;
using Weir.Contract.FakeTools;

namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>
/// What the fake ffprobe/ffmpeg do for the files a glob matches. Every property left null keeps the tool's default
/// behaviour for that step; <c>new FileRule()</c> is the plain default.
/// </summary>
public sealed record FileRule
{
    /// <summary>The ffprobe answer, when the file does not carry its own (see <see cref="FakeMedia.Bytes"/>).</summary>
    public JsonObject? Probe { get; init; }

    /// <summary>ffprobe exits 1 with this on stderr (never for Weir's own staged outputs).</summary>
    public string? ProbeError { get; init; }

    /// <summary>The ffmpeg <c>-f null</c> read-through fails with this.</summary>
    public string? IntegrityError { get; init; }

    /// <summary>The remux fails with this.</summary>
    public string? RemuxError { get; init; }

    /// <summary>The remux fails only for the first N remuxes of that file name.</summary>
    public int? RemuxFailTimes { get; init; }

    /// <summary>The remux takes this long; it writes a partial output first.</summary>
    public double? RemuxDelaySeconds { get; init; }

    /// <summary>The remux stays in progress until this path exists, so a test controls exactly how long (capped at two minutes).</summary>
    public string? RemuxReleaseFile { get; init; }

    /// <summary>The probe of the remuxed output, instead of the source's streams restricted to the mapped ones.</summary>
    public JsonObject? OutputProbe { get; init; }

    internal JsonObject ToJson()
    {
        var rule = new JsonObject();
        Set(rule, FakeToolProtocol.ProbeKey, Probe?.DeepClone());
        Set(rule, FakeToolProtocol.ProbeErrorKey, ProbeError);
        Set(rule, FakeToolProtocol.IntegrityErrorKey, IntegrityError);
        Set(rule, FakeToolProtocol.RemuxErrorKey, RemuxError);
        Set(rule, FakeToolProtocol.RemuxFailTimesKey, RemuxFailTimes);
        Set(rule, FakeToolProtocol.RemuxDelaySecondsKey, RemuxDelaySeconds);
        Set(rule, FakeToolProtocol.RemuxReleaseFileKey, RemuxReleaseFile);
        Set(rule, FakeToolProtocol.OutputProbeKey, OutputProbe?.DeepClone());
        return rule;
    }

    private static void Set(JsonObject rule, string key, JsonNode? value)
    {
        if (value is not null)
        {
            rule[key] = value;
        }
    }
}
