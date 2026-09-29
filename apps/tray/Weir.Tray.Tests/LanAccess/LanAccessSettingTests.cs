using Weir.Tray.LanAccess;
using Xunit;

namespace Weir.Tray.Tests.LanAccess;

/// <summary>
/// The saved LAN access choice decides whether Weir is offered to the network, so nothing but an explicit "on"
/// may read as on.
/// </summary>
public sealed class LanAccessSettingTests : IDisposable
{
    private readonly TempDirectory _temp = TempDirectory.AsWeirHome();

    public void Dispose() => _temp.Dispose();

    private string Home => _temp.Path;

    private string SettingPath => Path.Combine(Home, LanAccessSetting.FileName);

    [Fact]
    public void Nothing_saved_reads_as_no_choice_yet()
    {
        Assert.Null(LanAccessSetting.Read(Home, _ => { }));
    }

    [Fact]
    public void Other_devices_round_trips()
    {
        LanAccessSetting.Write(Home, ListenScope.OtherDevices);

        Assert.Equal(ListenScope.OtherDevices, LanAccessSetting.Read(Home, _ => { }));
    }

    [Fact]
    public void This_pc_only_round_trips()
    {
        LanAccessSetting.Write(Home, ListenScope.ThisPcOnly);

        Assert.Equal(ListenScope.ThisPcOnly, LanAccessSetting.Read(Home, _ => { }));
    }

    [Fact]
    public void A_later_choice_replaces_the_earlier_one_without_leaving_scratch_files()
    {
        LanAccessSetting.Write(Home, ListenScope.OtherDevices);
        LanAccessSetting.Write(Home, ListenScope.ThisPcOnly);

        Assert.Equal(ListenScope.ThisPcOnly, LanAccessSetting.Read(Home, _ => { }));
        Assert.Empty(Directory.GetFiles(Home, "*.tmp"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("o")]
    [InlineData("yes")]
    [InlineData("true")]
    public void A_file_that_does_not_say_on_reads_as_this_pc_only(string text)
    {
        File.WriteAllText(SettingPath, text);

        Assert.Equal(ListenScope.ThisPcOnly, LanAccessSetting.Read(Home, _ => { }));
    }

    [Fact]
    public void An_unreadable_file_reads_as_this_pc_only()
    {
        using var held = new FileStream(SettingPath, FileMode.Create, FileAccess.Write, FileShare.None);

        Assert.Equal(ListenScope.ThisPcOnly, LanAccessSetting.Read(Home, _ => { }));
    }

    [Fact]
    public void A_file_that_is_not_understood_says_so_in_the_log()
    {
        File.WriteAllText(SettingPath, "yes");
        var messages = new List<string>();

        LanAccessSetting.Read(Home, messages.Add);

        Assert.Contains(messages, m => m.Contains("does not recognise", StringComparison.Ordinal));
    }

    [Fact]
    public void Surrounding_whitespace_is_ignored()
    {
        File.WriteAllText(SettingPath, "  on\r\n");

        Assert.Equal(ListenScope.OtherDevices, LanAccessSetting.Read(Home, _ => { }));
    }
}
