using System.Xml;
using System.Xml.Linq;
using Weir.Core.Time;

namespace Weir.Core.Updates;

/// <summary>
/// Reads GitHub's public release feed (<see cref="ReleaseCatalog.ReleasesFeedUrl"/>) into releases. The feed names the tag, the
/// title and the time each release was last updated, and nothing of its files: each release is given the Windows installer
/// every release carries, at its download address.
/// </summary>
/// <remarks>
/// <see cref="GitHubReleaseRecord.PublishedAt"/> is the feed's updated time, which a later edit of the release moves. The feed
/// has no pre-release or draft flag either: <see cref="GitHubReleaseRecord.Prerelease"/> follows the version's own pre-release
/// part, so a stable-numbered tag that GitHub marks as a pre-release reads as stable.
/// </remarks>
public static class ReleaseFeed
{
    private const string TagLinkMarker = "/releases/tag/";
    private const long MostCharacters = 2_000_000;
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// The feed's releases. An entry whose tag is not a version, such as the <c>untagged-…</c> address of a draft, is left out.
    /// Throws <see cref="XmlException"/> when the text is not XML or is larger than a release feed can be.
    /// </summary>
    public static IReadOnlyList<GitHubReleaseRecord> Parse(string feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MostCharacters };
        using var reader = XmlReader.Create(new StringReader(feed), settings);
        var releases = new List<GitHubReleaseRecord>();
        foreach (var entry in XDocument.Load(reader).Descendants(Atom + "entry"))
        {
            if (Release(entry) is { } release)
            {
                releases.Add(release);
            }
        }

        return releases;
    }

    private static GitHubReleaseRecord? Release(XElement entry)
    {
        var link = entry.Elements(Atom + "link").Select(element => (string?)element.Attribute("href")).FirstOrDefault(href => href?.Contains(TagLinkMarker, StringComparison.Ordinal) == true);
        if (link is null)
        {
            return null;
        }

        var tag = Uri.UnescapeDataString(link[(link.IndexOf(TagLinkMarker, StringComparison.Ordinal) + TagLinkMarker.Length)..]);
        if (!SemanticVersion.TryParse(tag, out var parsed) || ReleaseCatalog.NormalizeReleaseVersion(tag) is not { } version)
        {
            return null;
        }

        var name = ((string?)entry.Element(Atom + "title"))?.Trim();
        var installer = new GitHubReleaseAsset(
            ReleaseCatalog.WindowsInstallerAssetName, ApiUrl: null, ReleaseCatalog.DownloadUrl(tag, ReleaseCatalog.WindowsInstallerAssetName), SizeBytes: 0, ContentType: null);

        // The feed has no pre-release flag, so a release is a pre-release when its tag has a semver pre-release suffix (-rc.1).
        return new GitHubReleaseRecord(
            tag, version, string.IsNullOrEmpty(name) ? null : name, ReleaseCatalog.TagUrl(tag), UpdatedAt(entry), Draft: false, parsed.IsPreRelease, [installer]);
    }

    private static Timestamp? UpdatedAt(XElement entry) =>
        ((string?)entry.Element(Atom + "updated"))?.Trim() is { Length: > 0 } updated
        && Timestamp.TryFromIsoFormat(updated.Replace("Z", "+00:00", StringComparison.Ordinal), out var parsed)
            ? parsed
            : null;
}
