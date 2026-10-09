using Microsoft.Win32;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// The mark that tells the after-install hook Weir was running when Setup stopped it. These run against a throw-away key
/// under HKCU, never the real mark.
/// </summary>
public sealed class TrayRunningMarkTests : IDisposable
{
    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly string _keyPath = $@"Software\WeirTrayMarkTests\{Guid.NewGuid():n}";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\WeirTrayMarkTests", throwOnMissingSubKey: false);
        _home.Dispose();
    }

    private TrayRunningMark Mark() => new(_keyPath);

    [Fact]
    public void Nothing_is_marked_before_a_tray_starts()
    {
        Assert.False(Mark().Take());
    }

    [Fact]
    public void A_tray_that_is_running_is_marked()
    {
        Mark().Set(1234);

        Assert.True(Mark().Take());
    }

    [Fact]
    public void Reading_the_mark_removes_it()
    {
        Mark().Set(1234);

        Mark().Take();

        Assert.False(Mark().Take());
    }

    [Fact]
    public void A_tray_that_exits_in_order_clears_its_mark()
    {
        Mark().Set(1234);

        Mark().Clear(1234);

        Assert.False(Mark().Take());
    }

    [Fact]
    public void A_tray_that_exits_does_not_clear_the_mark_of_one_started_since()
    {
        Mark().Set(1234);
        Mark().Set(5678);

        Mark().Clear(1234);

        Assert.True(Mark().Take());
    }

    [Fact]
    public void Clearing_a_mark_that_is_not_there_is_harmless()
    {
        Mark().Clear(1234);

        Assert.False(Mark().Take());
    }

    [Fact]
    public void The_mark_does_not_survive_a_restart_or_a_sign_out()
    {
        Mark().Set(1234);

        using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true)!;

        // Windows keeps a volatile key in memory only, so it is gone with the user's session.
        Assert.True(IsVolatile(key));
    }

    // .NET has no property for a key's volatility, but Windows refuses a stable subkey under a volatile one.
    private static bool IsVolatile(RegistryKey key)
    {
        try
        {
            key.CreateSubKey("stable", RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryOptions.None).Dispose();
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
