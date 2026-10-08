using System.Xml;
using System.Xml.Linq;
using Weir.Core.Time;

namespace Weir.Core.Updates;

/// <summary>
/// Reads GitHub's public release feed (<see cref="ReleaseCatalog.ReleasesFeedUrl"/>) into releases. The feed names the tag, the
/// title and the time of each published release, and nothing of its files, so the Windows installer is the download URL every
/// release's installer has by name.
/// </summary>
public static class ReleaseFeed
{
    private const string TagLinkMarker = "/releases/tag/";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    /// <summary>
    /// The feed's releases. An entry whose tag is not a version, such as the <c>untagged-…</c> address of a draft, is left out.
    /// Throws <see cref="XmlException"/> when the text is not XML.
    /// </summary>
    public static IReadOnlyList<GitHubReleaseRecord> Parse(string feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        using var reader = XmlReader.Create(new StringReader(feed), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
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
            ReleaseCatalog.WindowsInstallerAssetName, string.Empty, ReleaseCatalog.DownloadUrl(tag, ReleaseCatalog.WindowsInstallerAssetName), 0, null);
        return new GitHubReleaseRecord(
            tag, version, string.IsNullOrEmpty(name) ? null : name, link, PublishedAt(entry), Draft: false, parsed.IsPreRelease, [installer]);
    }

    private static Timestamp? PublishedAt(XElement entry) =>
        ((string?)entry.Element(Atom + "updated"))?.Trim() is { Length: > 0 } updated
        && Timestamp.TryFromIsoFormat(updated.Replace("Z", "+00:00", StringComparison.Ordinal), out var parsed)
            ? parsed
            : null;
}
