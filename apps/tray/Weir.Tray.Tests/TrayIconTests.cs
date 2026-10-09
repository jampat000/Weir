using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The brand icon, the dot in its bottom-right corner and the mark in its top-left.</summary>
public sealed class TrayIconTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40];

    private static readonly TrayDot[] Dots = [TrayDot.Starting, TrayDot.Green, TrayDot.Amber, TrayDot.Red];

    private static readonly TrayMark[] Marks = [TrayMark.Paused, TrayMark.UpdateReady];

    private static Bitmap Render(TrayIconKey key, int size)
    {
        using var brand = Program.LoadAppIcon(new Size(size, size));
        return TrayIconRenderer.Render(brand, key, size);
    }

    private static Bitmap Render(TrayDot? dot, TrayMark mark, int size) => Render(new TrayIconKey(dot, mark), size);

    private static bool Same(Bitmap a, Bitmap b)
    {
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    return false;
                }
            }
        }
        return true;
    }

    // Frame sizes in the icon file, read from its directory.
    private static List<int> FrameSizes()
    {
        var assembly = typeof(Program).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("weir-tray-icon.ico", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16();
        reader.ReadUInt16();
        var count = reader.ReadUInt16();
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = reader.ReadBytes(16);
            sizes.Add(entry[0] == 0 ? 256 : entry[0]);
        }
        return sizes;
    }

    [Fact]
    public void The_icon_file_has_a_frame_for_every_size_the_shell_asks_for()
    {
        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 128, 256], FrameSizes().Order());
    }

    [Fact]
    public void With_no_dot_and_no_mark_the_icon_is_the_brand_icon_untouched()
    {
        foreach (var size in Sizes)
        {
            using var brand = Program.LoadAppIcon(new Size(size, size));
            using var plain = brand.ToBitmap();
            using var drawn = Render(null, TrayMark.None, size);

            Assert.Equal(size, drawn.Width);
            Assert.True(SameVisibly(plain, drawn), $"{size}px");
        }
    }

    [Fact]
    public void Every_dot_changes_the_bottom_right_corner_and_leaves_the_rest_of_the_icon_alone()
    {
        foreach (var size in Sizes)
        {
            using var plain = Render(null, TrayMark.None, size);
            var centre = TrayIconRenderer.DotCentre(size);
            var markCentre = TrayIconRenderer.MarkCentre(size);
            foreach (var dot in Dots)
            {
                using var drawn = Render(dot, TrayMark.None, size);

                Assert.NotEqual(plain.GetPixel(centre.X, centre.Y), drawn.GetPixel(centre.X, centre.Y));
                Assert.Equal(plain.GetPixel(markCentre.X, markCentre.Y), drawn.GetPixel(markCentre.X, markCentre.Y));
                Assert.Equal(plain.GetPixel(2, 2), drawn.GetPixel(2, 2));
                Assert.Equal(plain.GetPixel(size - 3, 1), drawn.GetPixel(size - 3, 1));
            }
        }
    }

    [Fact]
    public void Every_mark_changes_the_top_left_corner_and_leaves_the_dot_corner_alone()
    {
        foreach (var size in Sizes)
        {
            using var plain = Render(null, TrayMark.None, size);
            var centre = TrayIconRenderer.MarkCentre(size);
            var dotCentre = TrayIconRenderer.DotCentre(size);
            foreach (var mark in Marks)
            {
                using var drawn = Render(null, mark, size);

                Assert.NotEqual(plain.GetPixel(centre.X, centre.Y), drawn.GetPixel(centre.X, centre.Y));
                Assert.Equal(plain.GetPixel(dotCentre.X, dotCentre.Y), drawn.GetPixel(dotCentre.X, dotCentre.Y));
                Assert.Equal(plain.GetPixel(size - 3, 1), drawn.GetPixel(size - 3, 1));
                Assert.Equal(plain.GetPixel(2, size - 3), drawn.GetPixel(2, size - 3));
            }
        }
    }

    [Fact]
    public void The_dot_is_green_amber_or_red_and_the_starting_dot_is_neither()
    {
        foreach (var size in Sizes)
        {
            var centre = TrayIconRenderer.DotCentre(size);
            using var green = Render(TrayDot.Green, TrayMark.None, size);
            using var amber = Render(TrayDot.Amber, TrayMark.None, size);
            using var red = Render(TrayDot.Red, TrayMark.None, size);
            using var starting = Render(TrayDot.Starting, TrayMark.None, size);

            var greenPixel = green.GetPixel(centre.X, centre.Y);
            var amberPixel = amber.GetPixel(centre.X, centre.Y);
            var redPixel = red.GetPixel(centre.X, centre.Y);
            var startingPixel = starting.GetPixel(centre.X, centre.Y);

            Assert.True(greenPixel.G > 150 && greenPixel.R < 100 && greenPixel.B < 130, $"{size}px green was {greenPixel}");
            Assert.True(amberPixel.R > 200 && amberPixel.G is > 130 and < 200 && amberPixel.B < 100, $"{size}px amber was {amberPixel}");
            Assert.True(redPixel.R > 200 && redPixel.G < 100 && redPixel.B < 100, $"{size}px red was {redPixel}");
            Assert.True(startingPixel.R < 80 && startingPixel.G < 80 && startingPixel.B < 80, $"{size}px starting was {startingPixel}");
        }
    }

    [Fact]
    public void The_update_mark_is_blue()
    {
        foreach (var size in Sizes)
        {
            // The disc is blue around a white arrow, so some of its pixels, in the top-left corner, are plainly blue.
            var corner = TrayIconRenderer.MarkCentre(size).X * 2;
            using var drawn = Render(null, TrayMark.UpdateReady, size);

            var blue = Enumerable.Range(0, corner)
                .SelectMany(y => Enumerable.Range(0, corner).Select(x => drawn.GetPixel(x, y)))
                .Count(pixel => pixel.B > 200 && pixel.R < 100 && pixel.G < 160);

            Assert.True(blue >= 4, $"{size}px had {blue} blue pixels");
        }
    }

    [Fact]
    public void Every_dot_and_mark_together_looks_different_from_every_other()
    {
        TrayDot?[] dots = [null, .. Dots.Cast<TrayDot?>()];
        TrayMark[] marks = [TrayMark.None, .. Marks];
        var keys = dots.SelectMany(dot => marks.Select(mark => new TrayIconKey(dot, mark))).ToList();

        foreach (var size in Sizes)
        {
            var drawn = keys.Select(key => Render(key, size)).ToList();
            try
            {
                for (var i = 0; i < drawn.Count; i++)
                {
                    for (var j = i + 1; j < drawn.Count; j++)
                    {
                        Assert.False(Same(drawn[i], drawn[j]), $"{keys[i]} and {keys[j]} look the same at {size}px");
                    }
                }
            }
            finally
            {
                drawn.ForEach(bitmap => bitmap.Dispose());
            }
        }
    }

    [Fact]
    public void The_dot_and_the_mark_have_a_light_outline_so_they_read_on_a_dark_icon_and_a_light_taskbar()
    {
        const int size = 32;
        using var drawn = Render(TrayDot.Red, TrayMark.Paused, size);
        var dot = TrayIconRenderer.DotCentre(size);
        var mark = TrayIconRenderer.MarkCentre(size);

        var dotRim = drawn.GetPixel(dot.X, size - 1);
        var markRim = drawn.GetPixel(mark.X, 0);

        Assert.True(dotRim.R > 200 && dotRim.G > 200 && dotRim.B > 200, $"dot outline was {dotRim}");
        Assert.True(markRim.R > 200 && markRim.G > 200 && markRim.B > 200, $"mark outline was {markRim}");
    }

    [Fact]
    public void The_icons_are_made_once_per_key_and_kept()
    {
        using var icons = new TrayIcons(size => Program.LoadAppIcon(size), new Size(16, 16));
        var red = new TrayIconKey(TrayDot.Red, TrayMark.None);

        var first = icons.For(red);

        Assert.Same(first, icons.For(red));
        Assert.NotSame(first, icons.For(new TrayIconKey(TrayDot.Red, TrayMark.Paused)));
        Assert.NotSame(first, icons.For(new TrayIconKey(TrayDot.Green, TrayMark.None)));
        Assert.NotSame(first, icons.For(new TrayIconKey(null, TrayMark.None)));
        Assert.Equal(16, first.Width);
    }

    [Fact]
    public void A_blink_swaps_between_two_icons_already_made_and_draws_nothing_new()
    {
        var drawn = 0;
        using var icons = new TrayIcons(
            size =>
            {
                drawn++;
                return Program.LoadAppIcon(size);
            },
            new Size(16, 16));
        var starting = new TrayState(ServerPhase.Starting, null, null, 9347);
        var blink = new TrayBlink();
        blink.Follow(starting.Dot);
        var seen = new HashSet<Icon>(ReferenceEqualityComparer.Instance);

        for (var tick = 0; tick < 200; tick++)
        {
            seen.Add(icons.For(starting.IconKey(blink.Lit)));
            blink.Tick();
        }

        Assert.Equal(2, seen.Count);
        Assert.Equal(2, drawn);
    }

    // The brand icon drawn onto a fresh bitmap can differ from Icon.ToBitmap in how transparent pixels are stored.
    private static bool SameVisibly(Bitmap a, Bitmap b)
    {
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var (p, q) = (a.GetPixel(x, y), b.GetPixel(x, y));
                if (Math.Abs(p.A - q.A) > 2 || (p.A > 0 && (Math.Abs(p.R - q.R) > 4 || Math.Abs(p.G - q.G) > 4 || Math.Abs(p.B - q.B) > 4)))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
