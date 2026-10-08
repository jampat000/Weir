using Xunit;

namespace Weir.Tray.Tests;

/// <summary>The brand icon and the corner mark drawn on it.</summary>
public sealed class TrayIconTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40];

    private static readonly TrayBadge[] Marks = [TrayBadge.Starting, TrayBadge.Paused, TrayBadge.NeedsYou, TrayBadge.UpdateReady];

    private static Bitmap Render(TrayBadge badge, int size)
    {
        using var brand = Program.LoadAppIcon(new Size(size, size));
        return TrayIconRenderer.Render(brand, badge, size);
    }

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
    public void With_no_mark_the_icon_is_the_brand_icon_untouched()
    {
        foreach (var size in Sizes)
        {
            using var brand = Program.LoadAppIcon(new Size(size, size));
            using var plain = brand.ToBitmap();
            using var drawn = Render(TrayBadge.None, size);

            Assert.Equal(size, drawn.Width);
            Assert.True(SameVisibly(plain, drawn), $"{size}px");
        }
    }

    [Fact]
    public void Every_mark_changes_the_corner_and_leaves_the_rest_of_the_icon_alone()
    {
        foreach (var size in Sizes)
        {
            using var plain = Render(TrayBadge.None, size);
            foreach (var mark in Marks)
            {
                using var marked = Render(mark, size);
                var centre = TrayIconRenderer.MarkCentre(size);

                Assert.NotEqual(plain.GetPixel(centre.X, centre.Y), marked.GetPixel(centre.X, centre.Y));
                Assert.Equal(plain.GetPixel(2, 2), marked.GetPixel(2, 2));
                Assert.Equal(plain.GetPixel(size / 2 - 1, 1), marked.GetPixel(size / 2 - 1, 1));
            }
        }
    }

    [Fact]
    public void The_needs_you_mark_is_red_and_the_update_mark_is_blue()
    {
        foreach (var size in Sizes)
        {
            var centre = TrayIconRenderer.MarkCentre(size);
            using var red = Render(TrayBadge.NeedsYou, size);
            using var blue = Render(TrayBadge.UpdateReady, size);

            var redPixel = red.GetPixel(centre.X, centre.Y);
            var bluePixel = blue.GetPixel(centre.X, centre.Y);

            Assert.True(redPixel.R > 200 && redPixel.G < 100 && redPixel.B < 100, $"{size}px red was {redPixel}");
            Assert.True(bluePixel.B > 200 && bluePixel.R < 100, $"{size}px blue was {bluePixel}");
        }
    }

    [Fact]
    public void Each_mark_looks_different_from_the_others()
    {
        foreach (var size in Sizes)
        {
            var drawn = Marks.Select(mark => Render(mark, size)).ToList();
            try
            {
                for (var i = 0; i < drawn.Count; i++)
                {
                    for (var j = i + 1; j < drawn.Count; j++)
                    {
                        Assert.False(Same(drawn[i], drawn[j]), $"{Marks[i]} and {Marks[j]} look the same at {size}px");
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
    public void The_mark_has_a_light_outline_so_it_reads_on_a_dark_icon_and_a_light_taskbar()
    {
        const int size = 32;
        using var marked = Render(TrayBadge.NeedsYou, size);
        var centre = TrayIconRenderer.MarkCentre(size);

        var rim = marked.GetPixel(centre.X, size - 1);

        Assert.True(rim.R > 200 && rim.G > 200 && rim.B > 200, $"outline was {rim}");
    }

    [Fact]
    public void The_icons_are_made_once_per_mark_and_kept()
    {
        using var icons = new TrayIcons(size => Program.LoadAppIcon(size), new Size(16, 16));

        var first = icons.For(TrayBadge.NeedsYou);

        Assert.Same(first, icons.For(TrayBadge.NeedsYou));
        Assert.NotSame(first, icons.For(TrayBadge.Paused));
        Assert.NotSame(icons.For(TrayBadge.None), first);
        Assert.Equal(16, first.Width);
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
