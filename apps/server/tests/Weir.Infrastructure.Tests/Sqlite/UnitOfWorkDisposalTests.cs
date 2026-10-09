using System.Text.RegularExpressions;

namespace Weir.Infrastructure.Tests.Sqlite;

/// <summary>
/// A unit of work that has written holds the database's write turn until it is committed, rolled back or disposed, so one that is
/// never disposed stops every other writer until Weir restarts. Every place the server opens one disposes it.
/// </summary>
public sealed partial class UnitOfWorkDisposalTests
{
    /// <summary>
    /// The places that dispose their unit some other way, each checked by hand: the request object disposes the one it opened
    /// (ApiRequest), start-up code disposes in a finally block (WeirServer), and the scan handler declares its results between
    /// opening the unit and the block that disposes it.
    /// </summary>
    private static readonly string[] DisposedAnotherWay = ["ApiRequest.cs", "WeirServer.cs", "LibraryScanHandler.cs"];

    [GeneratedRegex(@"await using\b")]
    private static partial Regex AwaitUsing();

    [Fact]
    public void Every_unit_of_work_the_server_opens_is_disposed_at_once()
    {
        var undisposed = new List<string>();
        var opened = 0;
        foreach (var file in Directory.EnumerateFiles(SourceFolder(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("UnitOfWork.OpenAsync(", StringComparison.Ordinal))
                {
                    continue;
                }

                opened++;
                var disposedAtOnce = lines.Skip(index).Take(3).Any(line => AwaitUsing().IsMatch(line));
                if (!disposedAtOnce && !DisposedAnotherWay.Contains(Path.GetFileName(file)))
                {
                    undisposed.Add($"{Path.GetFileName(file)}:{index + 1}");
                }
            }
        }

        Assert.True(opened > 50, "the scan found too few places to be looking at the right folder");
        Assert.Empty(undisposed);
    }

    private static string SourceFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var source = Path.Combine(folder.FullName, "src");
            if (Directory.Exists(Path.Combine(source, "Weir.Infrastructure")))
            {
                return source;
            }
        }

        throw new DirectoryNotFoundException("The server's source folder is not above the test output.");
    }
}
