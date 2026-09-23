using System.Security.AccessControl;
using System.Security.Principal;

using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// The runtime home holds the database, backups, logs and secrets, and under %ProgramData% every local user can
/// read it by default. The tray makes it owner-only on every start, and refuses a folder another account owns.
/// </summary>
public sealed class RuntimeHomeSecurityTests : IDisposable
{
    private static readonly SecurityIdentifier SomeoneElse = new("S-1-5-21-1111111111-2222222222-3333333333-1001");

    private readonly TempDirectory _parent = TempDirectory.Create();

    public void Dispose() => _parent.Dispose();

    private string Home => Path.Combine(_parent.Path, "Weir");

    [Fact]
    public void A_new_runtime_home_is_created_owner_only_for_everything_in_it()
    {
        RuntimeHomeSecurity.Secure(Home);

        var security = new DirectoryInfo(Home).GetAccessControl();
        Assert.True(OwnerOnlyAccess.IsOwnerOnlyForContents(security));
        Assert.Equal(OwnerOnlyAccess.AllowedAccounts(), SecretFileTests.Accounts(security));
    }

    [Fact]
    public void A_file_created_later_inherits_the_owner_only_list()
    {
        RuntimeHomeSecurity.Secure(Home);

        var database = Path.Combine(Home, "weir.sqlite3");
        File.WriteAllText(database, "");

        Assert.Equal(OwnerOnlyAccess.AllowedAccounts(), SecretFileTests.Accounts(new FileInfo(database).GetAccessControl()));
    }

    [Fact]
    public void A_looser_runtime_home_is_repaired_and_so_is_what_is_in_it()
    {
        Directory.CreateDirectory(Home);
        GrantEveryoneReadToContents(Home);
        var log = Path.Combine(Home, "tray-host.log");
        File.WriteAllText(log, "");
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.WorldSid, null), SecretFileTests.Accounts(new FileInfo(log).GetAccessControl()));

        var changed = RuntimeHomeSecurity.Secure(Home);

        Assert.True(changed);
        Assert.True(OwnerOnlyAccess.IsOwnerOnlyForContents(new DirectoryInfo(Home).GetAccessControl()));
        Assert.DoesNotContain(new SecurityIdentifier(WellKnownSidType.WorldSid, null), SecretFileTests.Accounts(new FileInfo(log).GetAccessControl()));
    }

    [Fact]
    public void An_owner_only_runtime_home_is_left_as_it_is()
    {
        RuntimeHomeSecurity.Secure(Home);

        Assert.False(RuntimeHomeSecurity.Secure(Home));
    }

    [Fact]
    public void A_folder_this_account_or_windows_owns_can_be_the_runtime_home()
    {
        var me = OwnerOnlyAccess.CurrentUser();

        Assert.True(OwnerOnlyAccess.IsTrustedOwner(me, me));
        Assert.True(OwnerOnlyAccess.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), me));
        Assert.True(OwnerOnlyAccess.IsTrustedOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), me));
    }

    [Fact]
    public void A_folder_another_account_owns_cannot_be_the_runtime_home()
    {
        Assert.False(OwnerOnlyAccess.IsTrustedOwner(SomeoneElse, OwnerOnlyAccess.CurrentUser()));
    }

    private static void GrantEveryoneReadToContents(string directory)
    {
        var info = new DirectoryInfo(directory);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
    }
}
