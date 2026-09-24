using Weir.Core.LibraryMode;
using Weir.Infrastructure.LibraryMode;
using Weir.Infrastructure.Processing.RemuxPass;

namespace Weir.Infrastructure.Tests.LibraryMode;

/// <summary>Link count detection on real files (issue #508's test list), including a real hard link.</summary>
public sealed class HardlinkInspectorTests : IDisposable
{
    private readonly string _folder = Path.Join(Path.GetTempPath(), "weir-hardlink-tests-" + Guid.NewGuid().ToString("N"));

    public HardlinkInspectorTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void An_ordinary_file_reports_a_link_count_of_one()
    {
        var path = Path.Join(_folder, "solo.mkv");
        File.WriteAllText(path, "content");

        Assert.Equal(1, PhysicalHardlinkInspector.Instance.LinkCount(path));
    }

    [Fact]
    public void A_real_hard_link_is_detected_and_skipped_by_the_policy()
    {
        var original = Path.Join(_folder, "Movie (2020).mkv");
        var link = Path.Join(_folder, "Movie (2020).seeding.mkv");
        File.WriteAllText(original, "content");

        if (!FileLifecycle.CreateHardLink(link, original))
        {
            return; // e.g. no admin/dev-mode privilege on this CI runner's filesystem; not what this test proves.
        }

        var linkCount = PhysicalHardlinkInspector.Instance.LinkCount(original);

        Assert.Equal(2, linkCount);
        Assert.True(HardlinkPolicy.Evaluate(linkCount, cleanHardlinkedFiles: false).Skip);
        Assert.False(HardlinkPolicy.Evaluate(linkCount, cleanHardlinkedFiles: true).Skip);
    }

    [Fact]
    public void A_missing_file_throws_rather_than_reporting_a_link_count()
    {
        var missing = Path.Join(_folder, "missing.mkv");
        Assert.ThrowsAny<IOException>(() => PhysicalHardlinkInspector.Instance.LinkCount(missing));
    }
}
