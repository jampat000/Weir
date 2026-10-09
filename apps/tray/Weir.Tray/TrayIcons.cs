using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Weir.Tray;

/// <summary>
/// Draws the brand icon with its dot in the bottom-right corner and, when there is one, its mark in the top-left. The corners
/// are drawn at the size the shell shows the icon at, from a copy of themselves drawn several times larger and scaled down,
/// with a thin light outline so they read on a light and a dark taskbar alike.
/// </summary>
static class TrayIconRenderer
{
    private const int Supersample = 8;

    // Each corner takes half the icon's width, with an outline a sixteenth of the icon wide.
    private const float CornerShare = 0.5f;
    private const float OutlineShare = 1f / 16f;

    private static readonly Color Outline = Color.White;
    private static readonly Color Green = Color.FromArgb(0x2F, 0xB3, 0x5A);
    private static readonly Color Amber = Color.FromArgb(0xF5, 0xA5, 0x24);
    private static readonly Color Red = Color.FromArgb(0xE5, 0x48, 0x4D);
    private static readonly Color Blue = Color.FromArgb(0x3B, 0x82, 0xF6);
    private static readonly Color Slate = Color.FromArgb(0x2B, 0x36, 0x3C);
    private static readonly Color Grey = Color.FromArgb(0xB4, 0xBE, 0xC4);

    // The update arrow, pointing down, as fractions of the disc it sits in.
    private static readonly PointF[] Arrow =
    [
        new(0.40f, 0.18f), new(0.60f, 0.18f), new(0.60f, 0.50f), new(0.78f, 0.50f), new(0.50f, 0.80f), new(0.22f, 0.50f), new(0.40f, 0.50f),
    ];

    /// <summary>The dot's centre, as a point of an icon <paramref name="size"/> pixels square.</summary>
    internal static Point DotCentre(int size) => new(size - Diameter(size) / 2, size - Diameter(size) / 2);

    /// <summary>The mark's centre, as a point of an icon <paramref name="size"/> pixels square.</summary>
    internal static Point MarkCentre(int size) => new(Diameter(size) / 2, Diameter(size) / 2);

    /// <summary>A copy of <paramref name="brand"/>, <paramref name="size"/> pixels square, with the dot and mark of <paramref name="key"/> on it.</summary>
    internal static Bitmap Render(Icon brand, TrayIconKey key, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.DrawIcon(brand, new Rectangle(0, 0, size, size));
        if (key.Dot is null && key.Mark == TrayMark.None)
        {
            return bitmap;
        }

        using var corners = DrawCorners(key, size);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(corners, new Rectangle(0, 0, size, size), 0, 0, corners.Width, corners.Height, GraphicsUnit.Pixel);
        return bitmap;
    }

    private static int Diameter(int size) => Math.Max(6, (int)Math.Round(size * CornerShare));

    private static Bitmap DrawCorners(TrayIconKey key, int size)
    {
        var layer = new Bitmap(size * Supersample, size * Supersample, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(layer);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var diameter = Diameter(size) * Supersample;
        var outline = Math.Max(1f, size * OutlineShare) * Supersample;
        if (key.Dot is { } dot)
        {
            var disc = new RectangleF(layer.Width - diameter, layer.Height - diameter, diameter, diameter);
            DrawDot(graphics, DrawRim(graphics, disc, outline), dot);
        }
        if (key.Mark != TrayMark.None)
        {
            DrawMark(graphics, DrawRim(graphics, new RectangleF(0, 0, diameter, diameter), outline), key.Mark);
        }
        return layer;
    }

    // Draws the outline's disc, and returns the disc inside it that the dot or mark fills.
    private static RectangleF DrawRim(Graphics graphics, RectangleF disc, float outline)
    {
        using var rim = new SolidBrush(Outline);
        graphics.FillEllipse(rim, disc);
        return RectangleF.Inflate(disc, -outline, -outline);
    }

    private static void DrawDot(Graphics graphics, RectangleF inner, TrayDot dot)
    {
        switch (dot)
        {
            case TrayDot.Green:
                Fill(graphics, inner, Green);
                break;
            case TrayDot.Amber:
                Fill(graphics, inner, Amber);
                break;
            case TrayDot.Red:
                Fill(graphics, inner, Red);
                break;
            case TrayDot.Starting:
            default:
                Fill(graphics, inner, Slate);
                DrawRing(graphics, inner);
                break;
        }
    }

    private static void DrawMark(Graphics graphics, RectangleF inner, TrayMark mark)
    {
        if (mark == TrayMark.Paused)
        {
            Fill(graphics, inner, Slate);
            DrawBars(graphics, inner);
        }
        else
        {
            Fill(graphics, inner, Blue);
            DrawArrow(graphics, inner);
        }
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

    // A white arrow pointing down, the way an update is fetched.
    private static void DrawArrow(Graphics graphics, RectangleF inner)
    {
        using var brush = new SolidBrush(Color.White);
        graphics.FillPolygon(brush, [.. Arrow.Select(point => new PointF(inner.X + point.X * inner.Width, inner.Y + point.Y * inner.Height))]);
    }
}

/// <summary>
/// The tray's icons, one per dot and mark, made when first asked for and kept for the run. An icon made from a drawing owns a
/// Windows icon handle that disposing the <see cref="Icon"/> does not release, so each is held with its handle and both
/// go together. A blinking dot swaps between two of these; it never draws another.
/// </summary>
sealed class TrayIcons(Func<Size, Icon> brandAt, Size size) : IDisposable
{
    private readonly Dictionary<TrayIconKey, OwnedIcon> _icons = [];

    internal Icon For(TrayIconKey key)
    {
        if (!_icons.TryGetValue(key, out var owned))
        {
            owned = Make(key);
            _icons[key] = owned;
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

    private OwnedIcon Make(TrayIconKey key)
    {
        var brand = brandAt(size);
        if (key.Dot is null && key.Mark == TrayMark.None)
        {
            return new OwnedIcon(brand, IntPtr.Zero);
        }
        using (brand)
        {
            using var bitmap = TrayIconRenderer.Render(brand, key, size.Width);
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
