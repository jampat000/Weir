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
    private readonly string _answerKey = $@"Software\WeirTrayTests\{Guid.NewGuid():n}";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\WeirTrayTests", throwOnMissingSubKey: false);
        _startupFolder.Dispose();
        _home.Dispose();
    }

    private StartupRegistration Registration(string? executable = Executable) =>
        new(_runKey, new StartWithWindowsAnswer(_answerKey), _startupFolder.Path, executable);

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
        Registration(@"C:\old\Weir.exe").Enable();

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

    [Fact]
    public void An_answer_at_the_first_run_is_recorded_whether_it_is_yes_or_no()
    {
        StartWithWindowsPrompt.AskOnFirstRun([], () => true, () => true, Registration());
        Assert.True(Registration().IsAnswered);

        new StartWithWindowsAnswer(_answerKey).Forget();
        Assert.False(Registration().IsAnswered);

        StartWithWindowsPrompt.AskOnFirstRun([], () => true, () => false, Registration());
        Assert.True(Registration().IsAnswered);
    }

    [Fact]
    public void A_first_run_that_asks_nothing_records_nothing()
    {
        StartWithWindowsPrompt.AskOnFirstRun(["--silent"], () => true, () => true, Registration());
        StartWithWindowsPrompt.AskOnFirstRun([], () => false, () => true, Registration());

        Assert.False(Registration().IsAnswered);
        Assert.False(Registration().IsEnabled);
    }

    [Fact]
    public void Ticking_and_unticking_the_menu_item_are_answers()
    {
        Registration().Toggle();
        Assert.True(Registration().IsAnswered);
        Assert.True(Registration().IsEnabled);

        new StartWithWindowsAnswer(_answerKey).Forget();
        Registration().Toggle();
        Assert.True(Registration().IsAnswered);
        Assert.False(Registration().IsEnabled);
    }

    [Fact]
    public void An_entry_nobody_chose_is_asked_about_once_and_a_yes_keeps_it()
    {
        Registration().Enable();
        var asked = 0;

        Assert.True(Registration().IsSetWithoutAnswer);
        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => true, () => { asked++; return true; }, Registration());
        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => true, () => { asked++; return true; }, Registration());

        Assert.Equal(1, asked);
        Assert.True(Registration().IsEnabled);
        Assert.True(Registration().IsAnswered);
        Assert.False(Registration().IsSetWithoutAnswer);
    }

    [Fact]
    public void An_entry_nobody_chose_is_removed_by_a_no_and_not_asked_about_again()
    {
        Registration().Enable();
        var asked = 0;

        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => true, () => { asked++; return false; }, Registration());
        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => true, () => { asked++; return true; }, Registration());

        Assert.Equal(1, asked);
        Assert.False(Registration().IsEnabled);
        Assert.Null(RunValue());
        Assert.True(Registration().IsAnswered);
    }

    [Fact]
    public void A_start_that_cannot_ask_changes_nothing_and_the_next_one_that_can_asks()
    {
        Registration().Enable();
        var asked = 0;
        bool Ask() { asked++; return true; }

        StartWithWindowsPrompt.AskIfSetWithoutAnswer(["--silent"], () => true, Ask, Registration());
        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => false, Ask, Registration());
        StartWithWindowsPrompt.AskIfSetWithoutAnswer(["--no-browser"], () => true, Ask, Registration());

        Assert.Equal(0, asked);
        Assert.True(Registration().IsEnabled);
        Assert.False(Registration().IsAnswered);

        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => true, Ask, Registration());

        Assert.Equal(1, asked);
        Assert.True(Registration().IsAnswered);
    }

    [Fact]
    public void The_start_the_entry_itself_makes_at_sign_in_never_asks()
    {
        Registration().Enable();
        var signInArguments = RunValue()!.Split(' ', 2)[1].Split(' ');
        var asked = false;

        StartWithWindowsPrompt.AskIfSetWithoutAnswer(signInArguments, () => true, () => { asked = true; return true; }, Registration());

        Assert.False(asked);
        Assert.True(Registration().IsEnabled);
    }

    [Fact]
    public void A_fresh_install_that_nobody_asked_has_no_entry_and_is_never_asked_about_one()
    {
        var asked = false;

        StartWithWindowsPrompt.AskOnFirstRun(["--silent"], () => true, () => { asked = true; return true; }, Registration());
        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => true, () => { asked = true; return true; }, Registration());

        Assert.False(asked);
        Assert.False(Registration().IsEnabled);
        Assert.False(Registration().IsAnswered);
    }

    [Fact]
    public void An_entry_the_person_chose_is_not_asked_about()
    {
        Registration().Choose(startWithWindows: true);
        var asked = false;

        StartWithWindowsPrompt.AskIfSetWithoutAnswer([], () => true, () => { asked = true; return false; }, Registration());

        Assert.False(asked);
        Assert.True(Registration().IsEnabled);
    }

    [Fact]
    public void An_update_neither_answers_the_question_nor_asks_it()
    {
        Registration().Enable();

        Registration().RefreshIfEnabled();

        Assert.True(Registration().IsSetWithoutAnswer);
    }

    [Fact]
    public void Uninstalling_removes_the_entry_and_the_answer_so_a_later_install_asks_again()
    {
        Registration().Choose(startWithWindows: true);

        Registration().Uninstall();

        Assert.False(Registration().IsEnabled);
        Assert.False(Registration().IsAnswered);
    }
}
