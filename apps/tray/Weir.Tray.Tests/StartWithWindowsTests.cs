using Microsoft.Win32;
using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// Nothing starts with Windows until the person says yes: at the first-run question, or from the tray menu. Updates
/// and installs never turn it on. These run against a throw-away key under HKCU, never the real Run key.
/// </summary>
public sealed class StartWithWindowsTests : IDisposable
{
    private const string Executable = @"C:\Users\someone\AppData\Local\Weir\current\Weir.exe";

    private readonly TempDirectory _home = TempDirectory.AsWeirHome();
    private readonly TempDirectory _startupFolder = TempDirectory.Create();
    private readonly string _runKey = $@"Software\WeirTrayTests\{Guid.NewGuid():n}";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\WeirTrayTests", throwOnMissingSubKey: false);
        _startupFolder.Dispose();
        _home.Dispose();
    }

    private StartupRegistration Registration(string? executable = Executable) => new(_runKey, _startupFolder.Path, executable);

    private string? RunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey);
        return key?.GetValue("Weir") as string;
    }

    [Fact]
    public void Nothing_starts_with_Windows_before_anyone_chooses()
    {
        Assert.False(Registration().IsEnabled);
        Assert.Null(RunValue());
    }

    [Fact]
    public void Turning_it_on_starts_Weir_at_sign_in_without_opening_a_browser()
    {
        Assert.True(Registration().Enable());

        Assert.True(Registration().IsEnabled);
        Assert.Equal($"\"{Executable}\" --no-browser", RunValue());
    }

    [Fact]
    public void The_menu_tick_switches_it_on_and_then_off()
    {
        var registration = Registration();

        registration.Toggle();
        Assert.True(registration.IsEnabled);

        registration.Toggle();
        Assert.False(registration.IsEnabled);
        Assert.Null(RunValue());
    }

    [Fact]
    public void Turning_it_off_removes_the_entry()
    {
        var registration = Registration();
        registration.Enable();

        registration.Disable();

        Assert.False(registration.IsEnabled);
        Assert.Null(RunValue());
    }

    [Fact]
    public void Turning_it_off_when_it_was_never_on_is_harmless()
    {
        Registration().Disable();

        Assert.False(Registration().IsEnabled);
    }

    [Fact]
    public void A_startup_folder_shortcut_would_start_a_second_copy_so_turning_it_on_removes_it()
    {
        var shortcut = Path.Combine(_startupFolder.Path, "Weir.lnk");
        File.WriteAllText(shortcut, "x");

        Registration().Enable();

        Assert.False(File.Exists(shortcut));
    }

    [Fact]
    public void Without_a_program_to_start_nothing_is_turned_on()
    {
        Assert.False(Registration(executable: null).Enable());
        Assert.False(Registration().IsEnabled);
    }

    [Fact]
    public void An_update_points_an_entry_that_is_there_at_the_new_program()
    {
        new StartupRegistration(_runKey, _startupFolder.Path, @"C:\old\Weir.exe").Enable();

        Registration().RefreshIfEnabled();

        Assert.Equal($"\"{Executable}\" --no-browser", RunValue());
    }

    [Fact]
    public void An_update_never_turns_it_on()
    {
        Registration().RefreshIfEnabled();

        Assert.False(Registration().IsEnabled);
        Assert.Null(RunValue());
    }

    [Fact]
    public void The_first_run_asks_once_and_a_yes_turns_it_on()
    {
        var asked = 0;

        StartWithWindowsPrompt.AskOnFirstRun([], () => true, () => { asked++; return true; }, Registration());

        Assert.Equal(1, asked);
        Assert.True(Registration().IsEnabled);
    }

    [Fact]
    public void A_no_leaves_it_off()
    {
        StartWithWindowsPrompt.AskOnFirstRun([], () => true, () => false, Registration());

        Assert.False(Registration().IsEnabled);
    }

    [Fact]
    public void A_silent_install_asks_nothing_and_leaves_it_off()
    {
        var asked = false;

        StartWithWindowsPrompt.AskOnFirstRun(["--silent"], () => true, () => { asked = true; return true; }, Registration());

        Assert.False(asked);
        Assert.False(Registration().IsEnabled);
    }

    [Fact]
    public void With_no_desktop_to_ask_on_it_asks_nothing_and_leaves_it_off()
    {
        var asked = false;

        StartWithWindowsPrompt.AskOnFirstRun([], () => false, () => { asked = true; return true; }, Registration());

        Assert.False(asked);
        Assert.False(Registration().IsEnabled);
    }

    [Fact]
    public void The_question_is_the_one_the_standard_words()
    {
        Assert.Equal("Start Weir when you sign in to Windows?", StartWithWindowsPrompt.Question);
    }
}
