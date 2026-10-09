using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Weir.Tray;

/// <summary>
/// Draws the brand icon with its one corner mark. The mark is drawn at the size the shell shows the icon at, from a
/// copy of itself drawn several times larger and scaled down, with a thin light outline so it reads on a light and a
/// dark taskbar alike.
/// </summary>
static class TrayIconRenderer
{
    private const int Supersample = 8;

    // The mark takes the bottom-right corner: half the icon wide, with an outline a sixteenth of the icon wide.
    private const float MarkShare = 0.5f;
    private const float OutlineShare = 1f / 16f;

    private static readonly Color Outline = Color.White;
    private static readonly Color Red = Color.FromArgb(0xE5, 0x48, 0x4D);
    private static readonly Color Blue = Color.FromArgb(0x3B, 0x82, 0xF6);
    private static readonly Color Slate = Color.FromArgb(0x2B, 0x36, 0x3C);
    private static readonly Color Grey = Color.FromArgb(0xB4, 0xBE, 0xC4);

    /// <summary>The mark's centre, as a point of an icon <paramref name="size"/> pixels square.</summary>
    internal static Point MarkCentre(int size) => new(size - MarkDiameter(size) / 2, size - MarkDiameter(size) / 2);

    /// <summary>A copy of <paramref name="brand"/>, <paramref name="size"/> pixels square, with the mark for <paramref name="badge"/> on it.</summary>
    internal static Bitmap Render(Icon brand, TrayBadge badge, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawIcon(brand, new Rectangle(0, 0, size, size));
        if (badge == TrayBadge.None)
        {
            return bitmap;
        }

        using var mark = DrawMark(badge, size);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(mark, new Rectangle(0, 0, size, size), 0, 0, mark.Width, mark.Height, GraphicsUnit.Pixel);
        return bitmap;
    }

    private static int MarkDiameter(int size) => Math.Max(6, (int)Math.Round(size * MarkShare));

    private static Bitmap DrawMark(TrayBadge badge, int size)
    {
        var scale = Supersample;
        var layer = new Bitmap(size * scale, size * scale, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(layer);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var diameter = MarkDiameter(size) * scale;
        var outline = Math.Max(1f, size * OutlineShare) * scale;
        var disc = new RectangleF(layer.Width - diameter, layer.Height - diameter, diameter, diameter);
        var inner = RectangleF.Inflate(disc, -outline, -outline);

        using (var rim = new SolidBrush(Outline))
        {
            graphics.FillEllipse(rim, disc);
        }

        switch (badge)
        {
            case TrayBadge.NeedsYou:
                Fill(graphics, inner, Red);
                break;
            case TrayBadge.UpdateReady:
                Fill(graphics, inner, Blue);
                break;
            case TrayBadge.Starting:
                Fill(graphics, inner, Slate);
                DrawRing(graphics, inner);
                break;
            case TrayBadge.Paused:
                Fill(graphics, inner, Slate);
                DrawBars(graphics, inner);
                break;
            case TrayBadge.None:
            default:
                break;
        }
        return layer;
    }

    private static void Fill(Graphics graphics, RectangleF area, Color colour)
    {
        using var brush = new SolidBrush(colour);
        graphics.FillEllipse(brush, area);
    }

    // A ring: the disc's own edge, in grey, a fifth of its width.
    private static void DrawRing(Graphics graphics, RectangleF inner)
    {
        var width = inner.Width / 5f;
        using var pen = new Pen(Grey, width);
        graphics.DrawEllipse(pen, RectangleF.Inflate(inner, -width / 2f, -width / 2f));
    }

    // Two upright bars, centred on the disc.
    private static void DrawBars(Graphics graphics, RectangleF inner)
    {
        var barWidth = inner.Width * 0.2f;
        var barHeight = inner.Height * 0.52f;
        var gap = inner.Width * 0.16f;
        var left = inner.X + (inner.Width - (2 * barWidth + gap)) / 2f;
        var top = inner.Y + (inner.Height - barHeight) / 2f;
        using var brush = new SolidBrush(Grey);
        graphics.FillRectangle(brush, left, top, barWidth, barHeight);
        graphics.FillRectangle(brush, left + barWidth + gap, top, barWidth, barHeight);
    }
}

/// <summary>
/// The tray's icons, one per mark, made when first asked for and kept for the run. An icon made from a drawing owns a
/// Windows icon handle that disposing the <see cref="Icon"/> does not release, so each is held with its handle and both
/// go together.
/// </summary>
sealed class TrayIcons(Func<Size, Icon> brandAt, Size size) : IDisposable
{
    private readonly Dictionary<TrayBadge, OwnedIcon> _icons = [];

    internal Icon For(TrayBadge badge)
    {
        if (!_icons.TryGetValue(badge, out var owned))
        {
            owned = Make(badge);
            _icons[badge] = owned;
        }
        return owned.Icon;
    }

    public void Dispose()
    {
        foreach (var owned in _icons.Values)
        {
            owned.Dispose();
        }
        _icons.Clear();
    }

    private OwnedIcon Make(TrayBadge badge)
    {
        var brand = brandAt(size);
        if (badge == TrayBadge.None)
        {
            return new OwnedIcon(brand, IntPtr.Zero);
        }
        using (brand)
        {
            using var bitmap = TrayIconRenderer.Render(brand, badge, size.Width);
            var handle = bitmap.GetHicon();
            return new OwnedIcon(Icon.FromHandle(handle), handle);
        }
    }

    private sealed class OwnedIcon(Icon icon, IntPtr handle) : IDisposable
    {
        internal Icon Icon { get; } = icon;

        public void Dispose()
        {
            Icon.Dispose();
            if (handle != IntPtr.Zero)
            {
                DestroyIcon(handle);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
