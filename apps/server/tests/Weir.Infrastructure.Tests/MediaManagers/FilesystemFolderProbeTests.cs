using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.MediaManagers;

namespace Weir.Infrastructure.Tests.MediaManagers;

/// <summary>
/// <see cref="FilesystemFolderProbe"/> against real folders on disk (#768) — the folder chain check's only
/// filesystem-touching implementation of <see cref="Core.MediaManagers.IFolderProbe"/>.
/// </summary>
public sealed class FilesystemFolderProbeTests
{
    private readonly FilesystemFolderProbe _probe = new();

    [Fact]
    public void A_real_folder_exists_and_a_missing_one_does_not()
    {
        using var temp = new TempDirectory();

        Assert.True(_probe.Exists(temp.Path));
        Assert.False(_probe.Exists(temp.Join("does-not-exist")));
    }

    [Fact]
    public void A_readable_folder_can_be_read()
    {
        using var temp = new TempDirectory();

        Assert.True(_probe.CanRead(temp.Path));
    }

    [Fact]
    public void A_writable_folder_can_be_written_to_and_the_probe_file_is_cleaned_up()
    {
        using var temp = new TempDirectory();

        var ok = _probe.CanWrite(temp.Path);

        Assert.True(ok);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [WindowsFact("Access control lists are a Windows behaviour.")]
    public void A_folder_with_a_deny_read_entry_cannot_be_read()
    {
        // [WindowsFact] skips this at runtime elsewhere; the check lets the platform analyzer (CA1416) see the ACL APIs as guarded.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var folder = Directory.CreateDirectory(temp.Join("Ready"));

        using (DenyReadAndExecute(folder))
        {
            Assert.True(_probe.Exists(folder.FullName));
            Assert.False(_probe.CanRead(folder.FullName));
        }

        Assert.True(_probe.CanRead(folder.FullName));
    }

    [WindowsFact("Access control lists are a Windows behaviour.")]
    public void An_output_folder_with_a_deny_read_entry_is_a_problem_that_is_not_probed_for_a_write()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var watched = Directory.CreateDirectory(temp.Join("Completed")).FullName;
        var work = Directory.CreateDirectory(temp.Join("Work")).FullName;
        var output = Directory.CreateDirectory(temp.Join("Ready"));

        IReadOnlyList<SetupCheckLine> lines;
        using (DenyReadAndExecute(output))
        {
            lines = LibraryFolderChainRules.CheckLocalFolders(watched, work, workFolderIsDefault: false, output.FullName, _probe);
        }

        Assert.Equal(
            $"Weir cannot read the output folder {output.FullName}. Check its permissions, or point this workflow at a folder Weir can read.",
            Assert.Single(lines, line => line.State == SetupCheckLine.Problem).Text);
        Assert.DoesNotContain(lines, line => line.State == SetupCheckLine.Ok && line.Text.Contains("the output folder", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.FullName));
    }

    [WindowsFact("Access control lists are a Windows behaviour.")]
    public void A_watched_folder_with_a_deny_read_entry_is_a_problem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var watched = Directory.CreateDirectory(temp.Join("Completed"));
        var work = Directory.CreateDirectory(temp.Join("Work")).FullName;
        var output = Directory.CreateDirectory(temp.Join("Ready")).FullName;

        IReadOnlyList<SetupCheckLine> lines;
        using (DenyReadAndExecute(watched))
        {
            lines = LibraryFolderChainRules.CheckLocalFolders(watched.FullName, work, workFolderIsDefault: false, output, _probe);
        }

        Assert.Equal(
            $"Weir cannot read the watched folder {watched.FullName}. Check its permissions, or point this workflow at a folder Weir can read.",
            Assert.Single(lines, line => line.State == SetupCheckLine.Problem).Text);
    }

    [WindowsFact("Access control lists are a Windows behaviour.")]
    public void A_work_folder_with_a_deny_read_entry_is_a_problem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var watched = Directory.CreateDirectory(temp.Join("Completed")).FullName;
        var work = Directory.CreateDirectory(temp.Join("Work"));
        var output = Directory.CreateDirectory(temp.Join("Ready")).FullName;

        IReadOnlyList<SetupCheckLine> lines;
        using (DenyReadAndExecute(work))
        {
            lines = LibraryFolderChainRules.CheckLocalFolders(watched, work.FullName, workFolderIsDefault: false, output, _probe);
        }

        Assert.Equal(
            $"Weir cannot read the work folder {work.FullName}. Check its permissions, or point this workflow's work folder at one Weir can read.",
            Assert.Single(lines, line => line.State == SetupCheckLine.Problem).Text);
    }

    /// <summary>The same entry <c>icacls &lt;folder&gt; /deny "&lt;account&gt;:(RX)"</c> adds, for the account the tests run as; disposing it takes the entry away again.</summary>
    [SupportedOSPlatform("windows")]
    private static DenyEntry DenyReadAndExecute(DirectoryInfo folder)
    {
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadAndExecute, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        var security = folder.GetAccessControl();
        security.AddAccessRule(rule);
        folder.SetAccessControl(security);
        return new DenyEntry(folder, rule);
    }

    [SupportedOSPlatform("windows")]
    private sealed class DenyEntry(DirectoryInfo folder, FileSystemAccessRule rule) : IDisposable
    {
        public void Dispose()
        {
            var security = folder.GetAccessControl();
            security.RemoveAccessRule(rule);
            folder.SetAccessControl(security);
        }
    }

    [Fact]
    public void Two_folders_under_the_same_root_share_a_filesystem()
    {
        using var temp = new TempDirectory();
        var first = temp.Join("first");
        var second = temp.Join("second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        Assert.True(_probe.SameFilesystem(first, second));
    }
}
