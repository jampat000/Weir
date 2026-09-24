using System.Globalization;
using System.Numerics;
using Weir.Core.Json;
using Weir.Core.Time;

namespace Weir.Core.Updates;

/// <summary>One asset of a GitHub release.</summary>
public sealed record GitHubReleaseAsset(string Name, string ApiUrl, string BrowserDownloadUrl, long SizeBytes, string? ContentType);

/// <summary>A GitHub release.</summary>
public sealed record GitHubReleaseRecord(
    string TagName,
    string Version,
    string? ReleaseName,
    string? HtmlUrl,
    Timestamp? PublishedAt,
    bool Draft,
    bool Prerelease,
    IReadOnlyList<GitHubReleaseAsset> Assets)
{
    public GitHubReleaseAsset? AssetNamed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var wanted = name.Trim().ToLowerInvariant();
        return Assets.FirstOrDefault(asset => string.Equals(asset.Name.Trim().ToLowerInvariant(), wanted, StringComparison.Ordinal));
    }

    public GitHubReleaseAsset? WindowsInstallerAsset() =>
        AssetNamed(ReleaseCatalog.WindowsInstallerAssetName) ?? AssetNamed(ReleaseCatalog.LegacyWindowsInstallerAssetName);
}

/// <summary>A failed GitHub release request; <see cref="ReleaseFetchException.StatusCode"/> is set when GitHub answered with an HTTP error.</summary>
public sealed class ReleaseFetchException : Exception
{
    public ReleaseFetchException()
    {
    }

    public ReleaseFetchException(string message)
        : base(message)
    {
    }

    public ReleaseFetchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ReleaseFetchException(int statusCode)
        : base($"GitHub answered HTTP {statusCode}.")
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}

/// <summary>Weir's GitHub releases: where they live, how versions compare, and reading the release API's payload.</summary>
public static class ReleaseCatalog
{
    public const string Owner = "jampat000";
    public const string Repo = "Weir";
    public const string LatestReleaseUrl = "https://api.github.com/repos/jampat000/Weir/releases/latest";
    public const string WindowsInstallerAssetName = "Weir-win-Setup.exe";
    public const string LegacyWindowsInstallerAssetName = "WeirSetup.exe";

    public static string? NormalizeReleaseVersion(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var text = raw.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var stripped = text.StartsWith('v') ? text[1..] : text;
        return stripped.Length == 0 ? null : stripped;
    }

    public static string TagForVersion(string version)
    {
        var normalized = NormalizeReleaseVersion(version) ?? throw new WireValueException("Release version is missing.");
        return "v" + normalized;
    }

    /// <summary>A comparable version key: leading numeric parts, each piece's digits concatenated.</summary>
    public static IReadOnlyList<BigInteger>? ParseVersionKey(string? raw)
    {
        var normalized = NormalizeReleaseVersion(raw);
        if (normalized is null)
        {
            return null;
        }

        var parts = new List<BigInteger>();
        foreach (var piece in normalized.Split('.'))
        {
            var digits = new string([.. piece.Where(char.IsDigit).Select(c => (char)('0' + (int)char.GetNumericValue(c)))]);
            if (digits.Length == 0)
            {
                break;
            }

            parts.Add(BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture));
        }

        return parts.Count > 0 ? parts : null;
    }

    /// <summary>Part by part; when one key is a prefix of the other, the shorter sorts first.</summary>
    public static int CompareVersionKeys(IReadOnlyList<BigInteger> left, IReadOnlyList<BigInteger> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            var cmp = left[i].CompareTo(right[i]);
            if (cmp != 0)
            {
                return cmp;
            }
        }

        return left.Count.CompareTo(right.Count);
    }

    /// <summary>Reads the release API's JSON into a <see cref="GitHubReleaseRecord"/>. Throws <see cref="WireValueException"/> for an unusable payload.</summary>
    public static GitHubReleaseRecord CoerceReleasePayload(WireValue payload)
    {
        if (payload is not WireObject dict)
        {
            throw new WireValueException("Release API returned an unexpected response.");
        }

        var tagName = OrText(dict.Get("tag_name")).Trim();
        var version = NormalizeReleaseVersion(tagName) ?? throw new WireValueException("Release metadata is missing a valid tag name.");
        Timestamp? publishedAt = null;
        if (dict.Get("published_at") is WireString published && published.Value.Trim().Length > 0)
        {
            if (!Timestamp.TryFromIsoFormat(published.Value.Trim().Replace("Z", "+00:00", StringComparison.Ordinal), out var parsed))
            {
                throw new WireValueException($"Invalid isoformat string: {WireStrings.Repr(published.Value.Trim())}");
            }

            publishedAt = parsed;
        }

        var assets = new List<GitHubReleaseAsset>();
        if (dict.Get("assets") is WireArray list)
        {
            foreach (var item in list.Items.OfType<WireObject>())
            {
                var name = OrText(item.Get("name")).Trim();
                var apiUrl = OrText(item.Get("url")).Trim();
                if (name.Length == 0 || apiUrl.Length == 0)
                {
                    throw new WireValueException("Release asset metadata is incomplete.");
                }

                var contentType = OrText(item.Get("content_type")).Trim();
                var size = item.Get("size") is { } sizeValue && sizeValue.IsTruthy ? WireConvert.ToInt(sizeValue) : BigInteger.Zero;
                assets.Add(new GitHubReleaseAsset(
                    name, apiUrl, OrText(item.Get("browser_download_url")).Trim(),
                    size > long.MaxValue ? long.MaxValue : (long)size,
                    contentType.Length == 0 ? null : contentType));
            }
        }

        var releaseName = OrText(dict.Get("name")).Trim();
        var htmlUrl = OrText(dict.Get("html_url")).Trim();
        return new GitHubReleaseRecord(
            tagName, version, releaseName.Length == 0 ? null : releaseName, htmlUrl.Length == 0 ? null : htmlUrl,
            publishedAt, dict.Get("draft")?.IsTruthy ?? false, dict.Get("prerelease")?.IsTruthy ?? false, assets);
    }

    private static string OrText(WireValue? value) => value is null || !value.IsTruthy ? string.Empty : WireConvert.Str(value);
}

/// <summary>The update status and update-settings payloads, and the tray's settings and state files.</summary>
public static class UpdateStatus
{
    public const string DockerImage = "ghcr.io/jampat000/weir";
    public static readonly IReadOnlyList<string> Modes = ["Auto", "DownloadOnly", "NotifyOnly"];

    public static WireObject Unavailable(string currentVersion, string installType, string status, string summary) =>
        Base(currentVersion, installType, status, summary)
            .Set("latest_version", (string?)null)
            .Set("latest_name", (string?)null)
            .Set("published_at", (string?)null)
            .Set("release_url", (string?)null)
            .Set("windows_installer_url", (string?)null)
            .Set("docker_image", installType == "docker" ? DockerImage : null)
            .Set("docker_tag", (string?)null)
            .Set("docker_update_command", (string?)null)
            .Set("in_app_upgrade_supported", installType == "windows")
            .Set("in_app_upgrade_summary", (string?)null);

    /// <summary>The update status once a release was fetched.</summary>
    public static WireObject FromRelease(string currentVersion, string installType, GitHubReleaseRecord release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var currentKey = ReleaseCatalog.ParseVersionKey(currentVersion);
        var latestKey = ReleaseCatalog.ParseVersionKey(release.Version);
        var updateAvailable = currentKey is not null && latestKey is not null && ReleaseCatalog.CompareVersionKeys(latestKey, currentKey) > 0;
        string status;
        string summary;
        if (release.Version.Length == 0)
        {
            status = "unavailable";
            summary = "Could not read the latest release version.";
        }
        else if (updateAvailable)
        {
            status = "update_available";
            summary = $"Weir {release.Version} is available.";
        }
        else
        {
            status = "up_to_date";
            summary = $"This install is already on Weir {currentVersion}.";
        }

        var installer = release.WindowsInstallerAsset();
        var dockerTag = release.Version.Length == 0 ? null : release.Version;
        return Base(currentVersion, installType, status, summary)
            .Set("latest_version", release.Version)
            .Set("latest_name", release.ReleaseName ?? release.Version)
            .Set("published_at", release.PublishedAt?.ToWireText())
            .Set("release_url", release.HtmlUrl)
            .Set("windows_installer_url", installer?.BrowserDownloadUrl)
            .Set("docker_image", installType == "docker" ? DockerImage : null)
            .Set("docker_tag", dockerTag)
            .Set("docker_update_command", installType == "docker" && dockerTag is not null ? "docker compose pull && docker compose up -d" : null)
            .Set("in_app_upgrade_supported", installType == "windows")
            .Set("in_app_upgrade_summary", installType == "windows" ? "Updates are managed by the Weir desktop app via Velopack." : null);
    }

    public static WireObject UpdateSettingsOut(string mode, bool checkOnStartup, long checkIntervalMinutes) => new WireObject()
        .Set("mode", mode)
        .Set("check_on_startup", checkOnStartup)
        .Set("check_interval_minutes", checkIntervalMinutes);

    public static readonly WireObject DefaultUpdateSettings = UpdateSettingsOut("Auto", true, 60);

    public static readonly WireObject UnreadableUpdateSettings = UpdateSettingsOut("NotifyOnly", true, 60);

    /// <summary>
    /// Reads <c>update-settings.json</c>'s text; <see langword="null"/> when it cannot be read and the
    /// notify-only fallback applies. Throws <see cref="WireTypeException"/> for valid JSON that is not an
    /// object.
    /// </summary>
    public static WireObject? ParseUpdateSettings(string text)
    {
        WireValue raw;
        try
        {
            raw = WireJsonParser.Parse(text);
        }
        catch (WireJsonDecodeException)
        {
            return null;
        }

        if (raw is not WireObject dict)
        {
            throw new WireTypeException("update-settings.json must hold a JSON object.");
        }

        var mode = WireConvert.Str(dict.Get("mode") ?? new WireString(string.Empty)).Trim();
        if (!Modes.Contains(mode, StringComparer.Ordinal))
        {
            return null;
        }

        var checkOnStartup = (dict.Get("checkOnStartup") ?? WireBool.True).IsTruthy;
        BigInteger interval;
        try
        {
            interval = WireConvert.ToInt(dict.Get("checkIntervalMinutes") ?? new WireInteger(60));
        }
        catch (Exception exception) when (exception is WireValueException or WireTypeException)
        {
            return null;
        }

        return interval < 1 || interval > 10080 ? null : UpdateSettingsOut(mode, checkOnStartup, (long)interval);
    }

    /// <summary>The <c>update-settings.json</c> the tray reads, as JSON indented by two spaces.</summary>
    public static string SerializeUpdateSettings(string mode, bool checkOnStartup, long checkIntervalMinutes) =>
        WireJsonWriter.Dumps(
            new WireObject().Set("mode", mode).Set("checkOnStartup", checkOnStartup).Set("checkIntervalMinutes", checkIntervalMinutes),
            WireJsonFormat.Indented);

    /// <summary>Reads the tray's update state file: whether an update is downloaded and its version, with a not-downloaded fallback.</summary>
    public static WireObject ParseUpdateState(string? text)
    {
        var fallback = new WireObject().Set("downloaded", false).Set("pending_version", (string?)null);
        if (text is null)
        {
            return fallback;
        }

        try
        {
            if (WireJsonParser.Parse(text) is not WireObject dict)
            {
                return fallback;
            }

            var version = dict.Get("version");
            string? pending = null;
            if (version is not null && version.IsTruthy)
            {
                if (version is not WireString s)
                {
                    return fallback;
                }

                pending = s.Value;
            }

            return new WireObject()
                .Set("downloaded", (dict.Get("downloaded") ?? WireBool.False).IsTruthy)
                .Set("pending_version", pending);
        }
        catch (WireJsonDecodeException)
        {
            return fallback;
        }
    }

    private static WireObject Base(string currentVersion, string installType, string status, string summary) => new WireObject()
        .Set("current_version", currentVersion)
        .Set("install_type", installType)
        .Set("status", status)
        .Set("summary", summary);
}
