using Xunit;

namespace Weir.Tray.Tests;

/// <summary>
/// The dialog's two options live in different layout containers, which WinForms does not make
/// mutually exclusive on its own: without the dialog's own handling, typing a port leaves "Use Weir's
/// default port" ticked too, and the dialog quietly uses the default.
/// </summary>
public sealed class PortDialogTests
{
    private static Func<int, bool> Busy(params int[] ports) => p => ports.Contains(p);

    private static void OnSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    [Fact]
    public void A_question_from_start_up_has_a_taskbar_button_and_is_top_most_but_the_one_from_the_menu_is_not()
    {
        OnSta(() =>
        {
            foreach (var reason in new[] { PortPromptReason.FirstRun, PortPromptReason.SavedPortBusy })
            {
                using var startUp = new PortDialog(new PortPrompt(reason, 9347, false, 9347), Busy(), null);

                Assert.True(startUp.MustBeSeen, reason.ToString());
                Assert.True(startUp.ShowInTaskbar, reason.ToString());
                Assert.True(startUp.TopMost, reason.ToString());
            }

            using var menu = new PortDialog(new PortPrompt(PortPromptReason.Change, 9347, false, 9347), Busy(), null);

            Assert.False(menu.MustBeSeen);
            Assert.False(menu.ShowInTaskbar);
            Assert.False(menu.TopMost);
        });
    }

    [Fact]
    public void A_question_that_must_be_seen_asks_Windows_for_a_top_most_window_before_it_is_shown()
    {
        // What Windows grants is its own decision; what Weir controls is what it asks for.
        const int topMostStyle = 0x00000008;
        OnSta(() =>
        {
            foreach (var reason in new[] { PortPromptReason.FirstRun, PortPromptReason.SavedPortBusy })
            {
                using var startUp = new PortDialog(new PortPrompt(reason, 9347, false, 9347), Busy(), null);

                Assert.False(startUp.IsHandleCreated, reason.ToString());
                Assert.True(startUp.TopMost, reason.ToString());
                Assert.NotEqual(0, startUp.RequestedExtendedStyle & topMostStyle);
            }

            using var menu = new PortDialog(new PortPrompt(PortPromptReason.Change, 9347, false, 9347), Busy(), null);

            Assert.False(menu.TopMost);
            Assert.Equal(0, menu.RequestedExtendedStyle & topMostStyle);
        });
    }

    [Fact]
    public void A_question_that_must_be_seen_is_brought_to_the_front_when_it_is_shown_and_the_one_from_the_menu_is_not()
    {
        OnSta(() =>
        {
            var windows = new RecordingWindows();
            using var startUp = new PortDialog(new PortPrompt(PortPromptReason.FirstRun, 9347, false, 9347), Busy(), null, windows);

            startUp.Show();
            Application.DoEvents();

            Assert.Equal([$"top {startUp.Handle}"], windows.Calls.Where(call => call.StartsWith("top ", StringComparison.Ordinal)));
            startUp.Close();

            var menuWindows = new RecordingWindows();
            using var menu = new PortDialog(new PortPrompt(PortPromptReason.Change, 9347, false, 9347), Busy(), null, menuWindows);

            menu.Show();
            Application.DoEvents();

            Assert.Empty(menuWindows.Calls);
            menu.Close();
        });
    }

    [Fact]
    public void First_run_with_the_default_free_starts_on_the_default()
    {
        OnSta(() =>
        {
            using var dialog = new PortDialog(new PortPrompt(PortPromptReason.FirstRun, 9347, false, 9347), Busy(), null);

            Assert.True(dialog.DefaultOption.Checked);
            Assert.False(dialog.CustomOption.Checked);
            dialog.PressOk();
            Assert.Equal(9347, dialog.ChosenPort);
        });
    }

    [Fact]
    public void Typing_a_port_switches_to_it_and_unticks_the_default()
    {
        OnSta(() =>
        {
            using var dialog = new PortDialog(new PortPrompt(PortPromptReason.Change, 9347, false, 9347), Busy(9347), null);

            dialog.CustomPortBox.Text = "9360";

            Assert.True(dialog.CustomOption.Checked);
            Assert.False(dialog.DefaultOption.Checked);
            Assert.Equal("Weir will be at http://localhost:9360/", dialog.AddressText);
            dialog.PressOk();
            Assert.Equal(9360, dialog.ChosenPort);
        });
    }

    [Fact]
    public void Choosing_the_default_again_unticks_the_custom_option()
    {
        OnSta(() =>
        {
            using var dialog = new PortDialog(new PortPrompt(PortPromptReason.FirstRun, 9347, false, 9347), Busy(), null);
            dialog.CustomPortBox.Text = "9400";

            dialog.DefaultOption.Checked = true;

            Assert.False(dialog.CustomOption.Checked);
            dialog.PressOk();
            Assert.Equal(9347, dialog.ChosenPort);
        });
    }

    [Fact]
    public void Busy_default_on_first_run_is_disabled_and_the_free_port_is_prefilled()
    {
        OnSta(() =>
        {
            using var dialog = new PortDialog(new PortPrompt(PortPromptReason.FirstRun, 9347, true, 9348), Busy(9347), null);

            Assert.False(dialog.DefaultOption.Enabled);
            Assert.Contains("in use", dialog.DefaultOption.Text);
            Assert.True(dialog.CustomOption.Checked);
            Assert.Equal("9348", dialog.CustomPortBox.Text);
        });
    }

    [Theory]
    [InlineData("70000", "A port is a whole number from 1 to 65535.")]
    [InlineData("abc", "A port is a whole number from 1 to 65535.")]
    [InlineData("", "Enter a port number.")]
    [InlineData("9400", "Another program on this computer is already using port 9400. Choose a different number.")]
    public void An_invalid_entry_keeps_the_dialog_open_and_says_why(string text, string error)
    {
        OnSta(() =>
        {
            using var dialog = new PortDialog(new PortPrompt(PortPromptReason.FirstRun, 9347, false, 9347), Busy(9400), null);
            dialog.CustomPortBox.Text = text;
            dialog.CustomOption.Checked = true;

            dialog.PressOk();

            Assert.Null(dialog.ChosenPort);
            Assert.Equal(error, dialog.ErrorText);
        });
    }
}
