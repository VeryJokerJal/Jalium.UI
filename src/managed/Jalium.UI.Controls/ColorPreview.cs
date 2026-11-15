using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Controls;
/// <summary>A replaceable color swatch template with a cached transparency surface.</summary>
public class ColorPreview : Control
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(ColorPreview), new PropertyMetadata(Color.Transparent));
    public Color Color { get => (Color)GetValue(ColorProperty)!; set => SetValue(ColorProperty, value); }

    static ColorPreview() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ColorPreview), new PropertyMetadata(typeof(ColorPreview)));
}

internal sealed class ColorPreviewSurface : Control
{
    internal static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(ColorPreviewSurface), new PropertyMetadata(Color.Transparent, OnColorChanged));
    public Color Color { get => (Color)GetValue(ColorProperty)!; set => SetValue(ColorProperty, value); }

    private readonly SolidColorBrush _colorBrush = new(Color.Transparent);
    private BitmapImage? _checker;
    private int _checkerWidth, _checkerHeight;
    private static readonly SolidColorBrush Light = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush Dark = new(Color.FromRgb(150, 150, 150));
    public ColorPreviewSurface()
    {
        Focusable = false;
        IsHitTestVisible = false;
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;
    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(RenderSize);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        DrawCheckerboard(dc, rect);
        _colorBrush.Color = Color;
        dc.DrawRectangle(_colorBrush, null, rect);
    }

    internal void DrawCheckerboard(DrawingContext dc, Rect rect)
    {
        int width = (int)Math.Round(rect.Width), height = (int)Math.Round(rect.Height);
        bool aligned = width > 0 && height > 0 && width <= 2048 && height <= 2048 && Math.Abs(rect.X - Math.Round(rect.X)) < .0001 && Math.Abs(rect.Y - Math.Round(rect.Y)) < .0001 && Math.Abs(rect.Width - width) < .0001 && Math.Abs(rect.Height - height) < .0001;
        if (aligned)
        {
            if (_checker == null || _checkerWidth != width || _checkerHeight != height)
            {
                var pixels = new byte[checked(width * height * 4)];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        byte value = (((x / 4) + (y / 4)) & 1) == 0 ? (byte)200 : (byte)150;
                        int offset = (y * width + x) * 4;
                        pixels[offset] = value;
                        pixels[offset + 1] = value;
                        pixels[offset + 2] = value;
                        pixels[offset + 3] = 255;
                    }

                _checker = BitmapImage.FromPixels(pixels, width, height, width * 4);
                _checkerWidth = width;
                _checkerHeight = height;
            }

            dc.DrawImage(_checker, rect);
            return;
        }

        dc.DrawRectangle(Light, null, rect);
        int row = 0;
        for (double y = rect.Y; y < rect.Bottom; y += 4, row++)
            for (double x = rect.X + ((row & 1) == 0 ? 4 : 0); x < rect.Right; x += 8)
                dc.DrawRectangle(Dark, null, new Rect(x, y, Math.Min(4, rect.Right - x), Math.Min(4, rect.Bottom - y)));
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorPreviewSurface)d).InvalidateVisual();
}
