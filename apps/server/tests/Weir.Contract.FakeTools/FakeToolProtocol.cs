namespace Weir.Contract.FakeTools;

/// <summary>
/// The names the fake tools and the tests that drive them agree on. This file is compiled into both
/// Weir.Contract.FakeTools and Weir.Contract.Tests, so neither side can drift from the other.
/// </summary>
internal static class FakeToolProtocol
{
    /// <summary>A file starting with these bytes carries its own ffprobe answer (JSON) after them.</summary>
    public const string MediaMagic = "FAKEMEDIA:";

    /// <summary>Overrides the folder holding the script and the logs; otherwise it is the folder of the tool itself.</summary>
    public const string ToolFolderVariable = "WEIR_CONTRACT_FAKE_TOOL_DIR";

    public const string ScriptFile = "script.json";
    public const string CallsFile = "calls.jsonl";
    public const string CountersFile = "counters.json";

    public const string FilesKey = "files";
    public const string DefaultKey = "default";

    public const string ProbeKey = "probe";
    public const string ProbeErrorKey = "probe_error";
    public const string IntegrityErrorKey = "integrity_error";
    public const string RemuxErrorKey = "remux_error";
    public const string RemuxFailTimesKey = "remux_fail_times";
    public const string RemuxDelaySecondsKey = "remux_delay_seconds";
    public const string RemuxReleaseFileKey = "remux_release_file";
    public const string OutputProbeKey = "output_probe";

    public const string ToolKey = "tool";
    public const string ArgvKey = "argv";
    public const string AtKey = "at";
    public const string StepKey = "step";
    public const string FileKey = "file";
    public const string AttemptKey = "attempt";

    public const string QueryStep = "query";
    public const string IntegrityStep = "integrity";
    public const string RemuxStep = "remux";
}
