using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Controls;
internal enum ColorSpectrumRegion
{
    None,
    SaturationValue,
    Hue
}

internal readonly record struct ColorSpectrumGeometry(Rect Square, Point Center, double OuterRadius, double InnerRadius)
{
    internal static ColorSpectrumGeometry Create(Size size, ColorSpectrumShape shape)
    {
        double side = Math.Max(0, Math.Min(size.Width, size.Height) - 12);
        var center = new Point(size.Width / 2, size.Height / 2);
        double radius = side / 2, inner = radius * .78;
        double squareSide = shape == ColorSpectrumShape.Ring ? inner * Math.Sqrt(2) * .92 : side;
        return new(new Rect(center.X - squareSide / 2, center.Y - squareSide / 2, squareSide, squareSide), center, radius, inner);
    }

    internal ColorSpectrumRegion HitTest(Point point, ColorSpectrumShape shape)
    {
        if (Square.Width <= 0 || Square.Height <= 0)
            return ColorSpectrumRegion.None;
        if (Square.Contains(point))
            return ColorSpectrumRegion.SaturationValue;
        if (shape != ColorSpectrumShape.Ring)
            return ColorSpectrumRegion.None;
        double x = point.X - Center.X, y = point.Y - Center.Y, distance = Math.Sqrt(x * x + y * y);
        return distance >= InnerRadius && distance <= OuterRadius ? ColorSpectrumRegion.Hue : ColorSpectrumRegion.None;
    }
}

/// <summary>Intrinsic drawing leaf used by the replaceable ColorSpectrum template.</summary>
internal sealed class ColorSpectrumSurface : Control
{
    internal static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double), typeof(ColorSpectrumSurface), new PropertyMetadata(0.0, OnPaintChanged));
    internal static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(ColorSpectrumSurface), new PropertyMetadata(1.0, OnPaintChanged));
    internal static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ColorSpectrumSurface), new PropertyMetadata(1.0, OnPaintChanged));
    internal static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(ColorSpectrumShape), typeof(ColorSpectrumSurface), new PropertyMetadata(ColorSpectrumShape.Box, OnPaintChanged));
    internal static readonly DependencyProperty SpectrumSizeProperty = DependencyProperty.Register(nameof(SpectrumSize), typeof(double), typeof(ColorSpectrumSurface), new PropertyMetadata(200.0, OnSizeChanged));
    public double Hue { get => (double)GetValue(HueProperty)!; set => SetValue(HueProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty)!; set => SetValue(SaturationProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
    public ColorSpectrumShape Shape { get => (ColorSpectrumShape)GetValue(ShapeProperty)!; set => SetValue(ShapeProperty, value); }
    public double SpectrumSize { get => (double)GetValue(SpectrumSizeProperty)!; set => SetValue(SpectrumSizeProperty, value); }

    private static readonly LinearGradientBrush SaturationGradient = new(Color.White, Color.Transparent, 0);
    private static readonly LinearGradientBrush ValueGradient = new(Color.Transparent, Color.Black, 90);
    private static readonly Pen LightSelector = new(new SolidColorBrush(Color.White), 2);
    private static readonly Pen DarkSelector = new(new SolidColorBrush(Color.Black), 1);
    private readonly SolidColorBrush _hueBrush = new(Color.Red);
    private Pen? _borderPen;
    private Brush? _borderBrush;
    private BitmapImage? _ring;
    private int _ringWidth, _ringHeight;
    private Size _ringSize;
    public ColorSpectrumSurface()
    {
        Focusable = false;
        IsHitTestVisible = false;
    }

    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;
    internal ColorSpectrumGeometry Geometry => ColorSpectrumGeometry.Create(RenderSize, Shape);

    protected override Size MeasureOverride(Size availableSize)
    {
        double side = double.IsFinite(SpectrumSize) ? Math.Max(0, SpectrumSize) : 200;
        if (double.IsFinite(availableSize.Width))
            side = Math.Min(side, Math.Max(0, availableSize.Width));
        if (double.IsFinite(availableSize.Height))
            side = Math.Min(side, Math.Max(0, availableSize.Height));
        return new Size(side, side);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var geometry = Geometry;
        var square = geometry.Square;
        if (square.Width <= 0 || square.Height <= 0)
            return;
        if (Shape == ColorSpectrumShape.Ring)
        {
            var ring = GetRingBitmap();
            if (ring != null)
                dc.DrawImage(ring, new Rect(RenderSize));
            double radians = ColorPickerColorMath.Hue(Hue) * Math.PI / 180;
            double radius = (geometry.OuterRadius + geometry.InnerRadius) / 2;
            DrawSelector(dc, new Point(geometry.Center.X + Math.Cos(radians) * radius, geometry.Center.Y + Math.Sin(radians) * radius));
        }

        _hueBrush.Color = ColorPickerColorMath.FromHsv(Hue, 1, 1);
        dc.DrawRectangle(_hueBrush, null, square);
        dc.DrawRectangle(SaturationGradient, null, square);
        dc.DrawRectangle(ValueGradient, null, square);
        if (BorderBrush is { } brush)
        {
            if (_borderPen == null || !ReferenceEquals(brush, _borderBrush))
            {
                _borderBrush = brush;
                _borderPen = new Pen(brush, 1);
            }

            dc.DrawRectangle(null, _borderPen, square);
        }

        DrawSelector(dc, new Point(square.X + ColorPickerColorMath.Unit(Saturation) * square.Width, square.Y + (1 - ColorPickerColorMath.Unit(Value)) * square.Height));
    }

    private static void DrawSelector(DrawingContext dc, Point point)
    {
        dc.DrawEllipse(null, DarkSelector, point, 6.5, 6.5);
        dc.DrawEllipse(null, LightSelector, point, 5, 5);
        dc.DrawEllipse(null, DarkSelector, point, 3.5, 3.5);
    }

    private BitmapImage? GetRingBitmap()
    {
        // Keep only the current raster. Resizing must not create an unbounded
        // process-wide cache of every size the user has visited.
        double scale = Math.Min(1, 2048 / Math.Max(1, Math.Max(RenderSize.Width, RenderSize.Height)));
        int width = (int)Math.Ceiling(Math.Max(0, RenderSize.Width) * scale);
        int height = (int)Math.Ceiling(Math.Max(0, RenderSize.Height) * scale);
        if (width == 0 || height == 0)
            return null;
        if (_ring != null && width == _ringWidth && height == _ringHeight && _ringSize.Equals(RenderSize))
            return _ring;
        var geometry = Geometry;
        var pixels = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double dx = (x + .5) * RenderSize.Width / width - geometry.Center.X;
                double dy = (y + .5) * RenderSize.Height / height - geometry.Center.Y;
                double radius = Math.Sqrt(dx * dx + dy * dy);
                double coverage = Math.Clamp(Math.Min(geometry.OuterRadius - radius, radius - geometry.InnerRadius) * scale + .5, 0, 1);
                if (coverage <= 0)
                    continue;
                double hue = Math.Atan2(dy, dx) * 180 / Math.PI;
                if (hue < 0)
                    hue += 360;
                var color = ColorPickerColorMath.FromHsv(hue, 1, 1);
                int offset = (y * width + x) * 4;
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = (byte)Math.Round(coverage * 255);
            }

        _ring = BitmapImage.FromPixels(pixels, width, height, width * 4);
        _ringWidth = width;
        _ringHeight = height;
        _ringSize = RenderSize;
        return _ring;
    }

    private static void OnPaintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorSpectrumSurface)d).InvalidateVisual();
    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ColorSpectrumSurface)d).InvalidateMeasure();
        ((ColorSpectrumSurface)d).InvalidateVisual();
    }
}
