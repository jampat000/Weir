using System.Globalization;
using System.Text.RegularExpressions;
using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// NZBGet's JSON-RPC <c>config</c> method, read with no I/O: a flat array of
/// <c>{"Name": "...", "Value": "..."}</c> option rows under <c>result</c>, holding the base completed folder
/// (<c>DestDir</c>) and each category's own (<c>CategoryN.Name</c> paired with <c>CategoryN.DestDir</c>).
/// </summary>
public static partial class NzbgetRules
{
    [GeneratedRegex(@"^Category(\d+)\.(Name|DestDir)$")]
    private static partial Regex CategoryOptionKey();

    public static DownloadClientFolders ParseConfig(WireValue? response)
    {
        var options = FlattenOptions(response);
        var destDir = options.GetValueOrDefault("DestDir");
        return new DownloadClientFolders(string.IsNullOrEmpty(destDir) ? null : destDir, ParseCategories(options));
    }

    private static List<DownloadClientCategoryFolder> ParseCategories(Dictionary<string, string> options)
    {
        var byIndex = new SortedDictionary<int, (string? Name, string? DestDir)>();
        foreach (var (key, value) in options)
        {
            var match = CategoryOptionKey().Match(key);
            if (!match.Success)
            {
                continue;
            }

            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var entry = byIndex.GetValueOrDefault(index);
            byIndex[index] = match.Groups[2].Value == "Name" ? (value, entry.DestDir) : (entry.Name, value);
        }

        return [.. byIndex.Values
            .Where(entry => !string.IsNullOrEmpty(entry.Name) && !string.IsNullOrEmpty(entry.DestDir))
            .Select(entry => new DownloadClientCategoryFolder(entry.Name!, entry.DestDir!))];
    }

    private static Dictionary<string, string> FlattenOptions(WireValue? response)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (response is not WireObject dict || dict.Get("result") is not WireArray result)
        {
            return options;
        }

        foreach (var row in result.Items.OfType<WireObject>())
        {
            if (ManagerValues.Text(row.Get("Name")) is { } name)
            {
                options[name] = ManagerValues.Text(row.Get("Value")) ?? string.Empty;
            }
        }

        return options;
    }
}
