using Xunit;

namespace Weir.Tray.Tests;

/// <summary>A stand-in for Windows that records what it is asked and answers as it is told to.</summary>
sealed class RecordingWindows : IWindowSystem
{
    private IntPtr _foreground = new(1);

    public List<string> Calls { get; } = [];

    /// <summary>Whether the first plain request for the foreground is allowed.</summary>
    public bool AllowsForeground { get; init; }

    /// <summary>Whether joining the holder's input lets the request through.</summary>
    public bool AllowsForegroundThroughTheHolder { get; init; }

    public void PutOnTop(IntPtr window) => Calls.Add($"top {window}");

    public bool SetForeground(IntPtr window)
    {
        Calls.Add($"foreground {window}");
        if (AllowsForeground)
        {
            _foreground = window;
        }

        return AllowsForeground;
    }

    public void TakeForegroundThroughTheHolder(IntPtr window)
    {
        Calls.Add($"holder {window}");
        if (AllowsForegroundThroughTheHolder)
        {
            _foreground = window;
        }
    }

    public IntPtr Foreground() => _foreground;

    public void Flash(IntPtr window) => Calls.Add($"flash {window}");
}

/// <summary>
/// What <see cref="ForegroundWindow.Bring(Form, IWindowSystem)"/> asks of Windows, and what it does when Windows says no. Windows'
/// own answers are faked: whether it grants top-most or the foreground depends on what the person is doing at the time.
/// </summary>
public sealed class ForegroundWindowTests
{
    private static void OnSta(Action<Form> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new Form { ShowInTaskbar = false };
                body(form);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
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
    public void The_window_is_made_top_most_and_then_asked_to_be_the_foreground_window()
    {
        OnSta(form =>
        {
            var windows = new RecordingWindows { AllowsForeground = true };

            ForegroundWindow.Bring(form, windows);

            Assert.Equal([$"top {form.Handle}", $"foreground {form.Handle}"], windows.Calls);
        });
    }

    [Fact]
    public void When_the_foreground_is_refused_it_is_asked_for_again_through_the_thread_that_holds_it()
    {
        OnSta(form =>
        {
            var windows = new RecordingWindows { AllowsForegroundThroughTheHolder = true };

            ForegroundWindow.Bring(form, windows);

            Assert.Equal([$"top {form.Handle}", $"foreground {form.Handle}", $"holder {form.Handle}"], windows.Calls);
        });
    }

    [Fact]
    public void When_the_foreground_cannot_be_taken_at_all_the_taskbar_button_flashes()
    {
        OnSta(form =>
        {
            var windows = new RecordingWindows();

            ForegroundWindow.Bring(form, windows);

            Assert.Equal([$"top {form.Handle}", $"foreground {form.Handle}", $"holder {form.Handle}", $"flash {form.Handle}"], windows.Calls);
        });
    }
}
