using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The icon that shows while a start-up question waits: up at once, blinking as Starting, gone when the tray's own comes.</summary>
public sealed class StartupIconTests
{
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
    public void The_icon_is_up_at_once_saying_the_server_is_starting()
    {
        OnSta(() =>
        {
            using var icon = new StartupIcon(size => Program.LoadAppIcon(size), new Size(16, 16), () => { });

            Assert.True(icon.Visible);
            Assert.Equal("Weir - Starting...", icon.Text);
            Assert.NotNull(icon.Current);
        });
    }

    [Fact]
    public void It_blinks_between_two_icons_that_were_made_once()
    {
        OnSta(() =>
        {
            var made = 0;
            using var icon = new StartupIcon(
                size =>
                {
                    made++;
                    return Program.LoadAppIcon(size);
                },
                new Size(16, 16),
                () => { });
            var seen = new HashSet<Icon?>(ReferenceEqualityComparer.Instance as IEqualityComparer<Icon?>) { icon.Current };

            for (var tick = 0; tick < 50; tick++)
            {
                icon.Tick();
                seen.Add(icon.Current);
            }

            Assert.Equal(2, seen.Count);
            Assert.Equal(2, made);
        });
    }

    [Fact]
    public void Disposing_it_takes_it_out_of_the_notification_area()
    {
        OnSta(() =>
        {
            var icon = new StartupIcon(size => Program.LoadAppIcon(size), new Size(16, 16), () => { });

            icon.Dispose();

            Assert.False(icon.Visible);
        });
    }
}
