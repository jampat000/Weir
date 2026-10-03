using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Weir.Contract.FakeTools;

/// <summary>What a test told the tools about each input file: the first <c>files</c> rule whose glob matches the base name, else <c>default</c>.</summary>
internal sealed class ToolScript(JsonObject script)
{
    public JsonObject RuleFor(string path)
    {
        var name = Path.GetFileName(path);
        if (script[FakeToolProtocol.FilesKey] is JsonObject files)
        {
            foreach (var (pattern, rule) in files)
            {
                if (Matches(pattern, name))
                {
                    return rule as JsonObject ?? new JsonObject();
                }
            }
        }

        return script[FakeToolProtocol.DefaultKey] as JsonObject ?? new JsonObject();
    }

    // The shell-style globs of Python's fnmatch: * ? and [set] or [!set], compared case-insensitively where the file system is.
    private static bool Matches(string pattern, string name)
    {
        var regex = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '*':
                    regex.Append(".*");
                    break;
                case '?':
                    regex.Append('.');
                    break;
                case '[':
                    var close = ClosingBracket(pattern, i);
                    if (close < 0)
                    {
                        regex.Append(Regex.Escape("["));
                        break;
                    }

                    var set = pattern[(i + 1)..close];
                    var negated = set.StartsWith('!');
                    var members = (negated ? set[1..] : set).Replace(@"\", @"\\", StringComparison.Ordinal);
                    regex.Append('[').Append(negated ? "^" : string.Empty).Append(members).Append(']');
                    i = close;
                    break;
                default:
                    regex.Append(Regex.Escape(pattern[i].ToString()));
                    break;
            }
        }

        regex.Append('$');
        var options = RegexOptions.Singleline | (OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None);
        return Regex.IsMatch(name, regex.ToString(), options, TimeSpan.FromSeconds(2));
    }

    // A ']' straight after '[' or '[!' is a member of the set, not its end.
    private static int ClosingBracket(string pattern, int open)
    {
        var next = open + 1;
        if (next < pattern.Length && pattern[next] == '!')
        {
            next++;
        }

        if (next < pattern.Length && pattern[next] == ']')
        {
            next++;
        }

        return pattern.IndexOf(']', Math.Min(next, pattern.Length));
    }
}
