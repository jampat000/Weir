using Weir.Core.Processing;
using Weir.Infrastructure.Settings;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.DirectPlay;

/// <summary>One device's answer for the Files-screen badge.</summary>
public sealed record DirectPlayBadge(string DeviceId, string DeviceName, string Verdict, IReadOnlyList<string> Reasons);

/// <summary>The operator's chosen direct-play devices, and each file's verdict for them (read-only per file).</summary>
public sealed class DirectPlayService
{
    private readonly SuiteSettingsStore _suiteSettings;

    public DirectPlayService(SuiteSettingsStore suiteSettings) => _suiteSettings = suiteSettings ?? throw new ArgumentNullException(nameof(suiteSettings));

    /// <summary>The ids of the devices the operator chose.</summary>
    public async Task<List<string>> SelectedDeviceIdsAsync(UnitOfWork uow)
    {
        var row = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        return [.. row.DirectPlayDevices.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0)];
    }

    /// <summary>Saves the operator's choice, keeping only ids the current device list knows, in its own order.</summary>
    public async Task<List<string>> SaveSelectedDeviceIdsAsync(UnitOfWork uow, IReadOnlyList<DeviceProfile> knownDevices, IReadOnlyList<string> selected)
    {
        var wanted = new HashSet<string>(selected, StringComparer.Ordinal);
        var kept = knownDevices.Select(p => p.Id).Where(wanted.Contains).ToList();
        var before = await _suiteSettings.EnsureAsync(uow).ConfigureAwait(false);
        await _suiteSettings.UpdateAsync(uow, before, before with { DirectPlayDevices = string.Join(",", kept) }).ConfigureAwait(false);
        return kept;
    }

    /// <summary>The profiles of the devices the operator chose.</summary>
    public async Task<IReadOnlyList<DeviceProfile>> SelectedProfilesAsync(UnitOfWork uow, IReadOnlyList<DeviceProfile> knownDevices)
    {
        var chosen = new HashSet<string>(await SelectedDeviceIdsAsync(uow).ConfigureAwait(false), StringComparer.Ordinal);
        return chosen.Count == 0 ? [] : [.. knownDevices.Where(p => chosen.Contains(p.Id))];
    }

    /// <summary>Whether an override device list file exists in the Weir home folder.</summary>
    public static bool IsCustomised(string? weirHome) =>
        !string.IsNullOrEmpty(weirHome) && File.Exists(Path.Combine(weirHome, DirectPlayEvaluation.OverrideFileName));

    /// <summary>The media facts a direct-play verdict needs, from a Files row.</summary>
    public static MediaFacts FactsForRow(ProcessingFileRecord row) => new(
        DirectPlayEvaluation.ContainerForPath(row.RelativePath),
        row.VideoCodec,
        row.VideoHeight,
        row.VideoBitDepth,
        row.AudioCodecs is { } codecs ? [.. codecs.Split(',').Where(c => c.Length > 0)] : null);

    /// <summary>One direct-play badge per device for a Files row; empty when no device is chosen.</summary>
    public static List<DirectPlayBadge> ForRow(ProcessingFileRecord row, IReadOnlyList<DeviceProfile> devices)
    {
        if (devices.Count == 0)
        {
            return [];
        }

        var facts = FactsForRow(row);
        return [.. devices.Select(profile => DirectPlayEvaluation.Evaluate(profile, facts))
            .Select(verdict => new DirectPlayBadge(verdict.DeviceId, verdict.DeviceName, verdict.Verdict, verdict.Reasons))];
    }
}
