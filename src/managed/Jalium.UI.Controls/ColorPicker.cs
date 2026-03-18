using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that allows the user to select a color.
/// </summary>
public class ColorPicker : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ColorPickerAutomationPeer(this);
    }

    // Cached brushes and pens for OnRender
    private static readonly SolidColorBrush s_whiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_grayBorderBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush s_checkerLightBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush s_checkerDarkBrush = new(Color.FromRgb(150, 150, 150));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Color dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(nameof(Color), typeof(Color), typeof(ColorPicker),
            new PropertyMetadata(Color.White, OnColorChanged));

    /// <summary>
    /// Identifies the IsAlphaEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsAlphaEnabledProperty =
        DependencyProperty.Register(nameof(IsAlphaEnabled), typeof(bool), typeof(ColorPicker),
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the IsColorSpectrumVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsColorSpectrumVisibleProperty =
        DependencyProperty.Register(nameof(IsColorSpectrumVisible), typeof(bool), typeof(ColorPicker),
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the IsColorSliderVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsColorSliderVisibleProperty =
        DependencyProperty.Register(nameof(IsColorSliderVisible), typeof(bool), typeof(ColorPicker),
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the IsColorPreviewVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsColorPreviewVisibleProperty =
        DependencyProperty.Register(nameof(IsColorPreviewVisible), typeof(bool), typeof(ColorPicker),
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the IsHexInputVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsHexInputVisibleProperty =
        DependencyProperty.Register(nameof(IsHexInputVisible), typeof(bool), typeof(ColorPicker),
            new PropertyMetadata(true, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ColorSpectrumShape dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ColorSpectrumShapeProperty =
        DependencyProperty.Register(nameof(ColorSpectrumShape), typeof(ColorSpectrumShape), typeof(ColorPicker),
            new PropertyMetadata(ColorSpectrumShape.Box, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the IsCompact dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(ColorPicker),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ColorChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ColorChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ColorChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<ColorChangedEventArgs>), typeof(ColorPicker));

    /// <summary>
    /// Occurs when the color changes.
    /// </summary>
    public event EventHandler<ColorChangedEventArgs> ColorChanged
    {
        add => AddHandler(ColorChangedEvent, value);
        remove => RemoveHandler(ColorChangedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the selected color.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Color Color
    {
        get => (Color)GetValue(ColorProperty)!;
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the alpha channel can be edited.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsAlphaEnabled
    {
        get => (bool)GetValue(IsAlphaEnabledProperty)!;
        set => SetValue(IsAlphaEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the color spectrum is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsColorSpectrumVisible
    {
        get => (bool)GetValue(IsColorSpectrumVisibleProperty)!;
        set => SetValue(IsColorSpectrumVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the color slider is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsColorSliderVisible
    {
        get => (bool)GetValue(IsColorSliderVisibleProperty)!;
        set => SetValue(IsColorSliderVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the color preview is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsColorPreviewVisible
    {
        get => (bool)GetValue(IsColorPreviewVisibleProperty)!;
        set => SetValue(IsColorPreviewVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the hex input is visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsHexInputVisible
    {
        get => (bool)GetValue(IsHexInputVisibleProperty)!;
        set => SetValue(IsHexInputVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the shape of the color spectrum.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public ColorSpectrumShape ColorSpectrumShape
    {
        get => (ColorSpectrumShape)(GetValue(ColorSpectrumShapeProperty) ?? ColorSpectrumShape.Box);
        set => SetValue(ColorSpectrumShapeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the color picker is displayed in compact mode.
    /// In compact mode, the spectrum is smaller and the preview/hex input are hidden.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty)!;
        set => SetValue(IsCompactProperty, value);
    }

    #endregion

    #region Private Fields

    private const double SpectrumSize = 200;
    private const double CompactSpectrumSize = 120;
    private const double SliderHeight = 20;
    private const double CompactSliderHeight = 14;
    private const double PreviewSize = 40;
    private const double Spacing = 8;
    private const double CompactSpacing = 4;

    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private byte _alpha = 255;

    private Rect _spectrumRect;
    private Rect _hueSliderRect;
    private Rect _alphaSliderRect;
    private Rect _previewRect;

    private bool _isDraggingSpectrum;
    private bool _isDraggingHue;
    private bool _isDraggingAlpha;
    private bool _isUpdatingColor;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorPicker"/> class.
    /// </summary>
    public ColorPicker()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));

        UpdateFromColor(Color);
    }

    #endregion

    protected override bool ShouldSuppressAutomaticTransition(DependencyProperty dp)
    {
        if (ReferenceEquals(dp, ColorProperty) &&
            (_isUpdatingColor || _isDraggingSpectrum || _isDraggingHue || _isDraggingAlpha))
        {
            return true;
        }

        return base.ShouldSuppressAutomaticTransition(dp);
    }

    #region Color Conversion

    private void UpdateFromColor(Color color)
    {
        _alpha = color.A;
        RgbToHsv(color.R, color.G, color.B, out _hue, out _saturation, out _value);
    }

    private Color GetCurrentColor()
    {
        HsvToRgb(_hue, _saturation, _value, out var r, out var g, out var b);
        return Color.FromArgb(_alpha, r, g, b);
    }

    private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == rd)
        {
            h = 60 * (((gd - bd) / delta) % 6);
        }
        else if (max == gd)
        {
            h = 60 * (((bd - rd) / delta) + 2);
        }
        else
        {
            h = 60 * (((rd - gd) / delta) + 4);
        }

        if (h < 0) h += 360;
        if (h >= 360) h -= 360;
    }

    private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double rd, gd, bd;

        if (h < 60) { rd = c; gd = x; bd = 0; }
        else if (h < 120) { rd = x; gd = c; bd = 0; }
        else if (h < 180) { rd = 0; gd = c; bd = x; }
        else if (h < 240) { rd = 0; gd = x; bd = c; }
        else if (h < 300) { rd = x; gd = 0; bd = c; }
        else { rd = c; gd = 0; bd = x; }

        r = (byte)Math.Round((rd + m) * 255);
        g = (byte)Math.Round((gd + m) * 255);
        b = (byte)Math.Round((bd + m) * 255);
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            CaptureMouse();
            var position = e.GetPosition(this);

            if (_spectrumRect.Contains(position))
            {
                _isDraggingSpectrum = true;
                UpdateSpectrumFromPosition(position);
            }
            else if (_hueSliderRect.Contains(position))
            {
                _isDraggingHue = true;
                UpdateHueFromPosition(position);
            }
            else if (_alphaSliderRect.Contains(position) && IsAlphaEnabled)
            {
                _isDraggingAlpha = true;
                UpdateAlphaFromPosition(position);
            }

            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ReleaseMouseCapture();
            _isDraggingSpectrum = false;
            _isDraggingHue = false;
            _isDraggingAlpha = false;
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(this);

        if (_isDraggingSpectrum)
        {
            UpdateSpectrumFromPosition(position);
        }
        else if (_isDraggingHue)
        {
            UpdateHueFromPosition(position);
        }
        else if (_isDraggingAlpha)
        {
            UpdateAlphaFromPosition(position);
        }
    }

    private void UpdateSpectrumFromPosition(Point position)
    {
        _saturation = Math.Clamp((position.X - _spectrumRect.X) / _spectrumRect.Width, 0, 1);
        _value = Math.Clamp(1 - (position.Y - _spectrumRect.Y) / _spectrumRect.Height, 0, 1);
        UpdateColor();
    }

    private void UpdateHueFromPosition(Point position)
    {
        _hue = Math.Clamp((position.X - _hueSliderRect.X) / _hueSliderRect.Width, 0, 1) * 360;
        UpdateColor();
    }

    private void UpdateAlphaFromPosition(Point position)
    {
        var alpha = Math.Clamp((position.X - _alphaSliderRect.X) / _alphaSliderRect.Width, 0, 1);
        _alpha = (byte)Math.Round(alpha * 255);
        UpdateColor();
    }

    private void UpdateColor()
    {
        var newColor = GetCurrentColor();
        _isUpdatingColor = true;
        try
        {
            Color = newColor;
        }
        finally
        {
            _isUpdatingColor = false;
        }
        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var compact = IsCompact;
        var specSize = compact ? CompactSpectrumSize : SpectrumSize;
        var sliderH = compact ? CompactSliderHeight : SliderHeight;
        var spacing = compact ? CompactSpacing : Spacing;

        var width = specSize + padding.TotalWidth;
        var height = padding.Top;

        if (IsColorSpectrumVisible)
        {
            height += specSize + spacing;
        }

        if (IsColorSliderVisible)
        {
            height += sliderH + spacing;
        }

        if (IsAlphaEnabled && IsColorSliderVisible)
        {
            height += sliderH + spacing;
        }

        if (!compact && IsColorPreviewVisible)
        {
            height += PreviewSize;
        }

        height += padding.Bottom;

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;
        var compact = IsCompact;
        var specSize = compact ? CompactSpectrumSize : SpectrumSize;
        var sliderH = compact ? CompactSliderHeight : SliderHeight;
        var spacing = compact ? CompactSpacing : Spacing;

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        var currentY = padding.Top;

        // Draw color spectrum
        if (IsColorSpectrumVisible)
        {
            _spectrumRect = new Rect(padding.Left, currentY, specSize, specSize);
            DrawColorSpectrum(dc, _spectrumRect);
            currentY += specSize + spacing;
        }

        // Draw hue slider
        if (IsColorSliderVisible)
        {
            _hueSliderRect = new Rect(padding.Left, currentY, specSize, sliderH);
            DrawHueSlider(dc, _hueSliderRect);
            currentY += sliderH + spacing;
        }

        // Draw alpha slider
        if (IsAlphaEnabled && IsColorSliderVisible)
        {
            _alphaSliderRect = new Rect(padding.Left, currentY, specSize, sliderH);
            DrawAlphaSlider(dc, _alphaSliderRect);
            currentY += sliderH + spacing;
        }

        // Draw preview (hidden in compact mode)
        if (!compact && IsColorPreviewVisible)
        {
            _previewRect = new Rect(padding.Left, currentY, PreviewSize, PreviewSize);
            DrawPreview(dc, _previewRect);

            // Draw hex value
            if (IsHexInputVisible)
            {
                var hexText = $"#{_alpha:X2}{Color.R:X2}{Color.G:X2}{Color.B:X2}";
                var formattedText = new FormattedText(hexText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 14)
                {
                    Foreground = ResolveForegroundBrush()
                };
                TextMeasurement.MeasureText(formattedText);

                dc.DrawText(formattedText, new Point(_previewRect.Right + spacing, currentY + (PreviewSize - formattedText.Height) / 2));
            }
        }
    }

    private void DrawColorSpectrum(DrawingContext dc, Rect rect)
    {
        // Draw spectrum background (simplified - in a real implementation this would be a shader or bitmap)
        // For now, draw a solid color at the current hue
        HsvToRgb(_hue, 1, 1, out var r, out var g, out var b);
        var hueColor = Color.FromRgb(r, g, b);

        // Draw base color
        dc.DrawRectangle(new SolidColorBrush(hueColor), null, rect);

        // Draw white-to-transparent gradient (saturation)
        var satGradient = new LinearGradientBrush(Color.White, Color.Transparent, 0);
        dc.DrawRectangle(satGradient, null, rect);

        // Draw black-to-transparent gradient (value)
        var valGradient = new LinearGradientBrush(Color.Transparent, Color.Black, 90);
        dc.DrawRectangle(valGradient, null, rect);

        // Draw border
        dc.DrawRectangle(null, GetBorderPen(), rect);

        // Draw selector
        var selectorX = rect.X + _saturation * rect.Width;
        var selectorY = rect.Y + (1 - _value) * rect.Height;
        dc.DrawEllipse(null, GetSelectorPen(), new Point(selectorX, selectorY), 6, 6);
    }

    private void DrawHueSlider(DrawingContext dc, Rect rect)
    {
        // Draw hue gradient
        for (int i = 0; i < 6; i++)
        {
            var startHue = i * 60;
            var endHue = (i + 1) * 60;
            HsvToRgb(startHue, 1, 1, out var sr, out var sg, out var sb);
            HsvToRgb(endHue, 1, 1, out var er, out var eg, out var eb);

            var segmentRect = new Rect(
                rect.X + rect.Width * i / 6,
                rect.Y,
                rect.Width / 6 + 1,
                rect.Height);

            var gradient = new LinearGradientBrush(
                Color.FromRgb(sr, sg, sb),
                Color.FromRgb(er, eg, eb), 0);
            dc.DrawRectangle(gradient, null, segmentRect);
        }

        // Draw border
        dc.DrawRoundedRectangle(null, GetBorderPen(), rect, 2, 2);

        // Draw selector
        var selectorX = rect.X + (_hue / 360) * rect.Width;
        var selectorRect = new Rect(selectorX - 2, rect.Y - 2, 4, rect.Height + 4);
        dc.DrawRoundedRectangle(ResolveForegroundBrush(), null, selectorRect, 2, 2);
    }

    private void DrawAlphaSlider(DrawingContext dc, Rect rect)
    {
        // Draw checkerboard pattern for transparency
        DrawCheckerboard(dc, rect);

        // Draw alpha gradient
        HsvToRgb(_hue, _saturation, _value, out var r, out var g, out var b);
        var transparent = Color.FromArgb(0, r, g, b);
        var opaque = Color.FromArgb(255, r, g, b);
        var gradient = new LinearGradientBrush(transparent, opaque, 0);
        dc.DrawRectangle(gradient, null, rect);

        // Draw border
        dc.DrawRoundedRectangle(null, GetBorderPen(), rect, 2, 2);

        // Draw selector
        var selectorX = rect.X + (_alpha / 255.0) * rect.Width;
        var selectorRect = new Rect(selectorX - 2, rect.Y - 2, 4, rect.Height + 4);
        dc.DrawRoundedRectangle(ResolveForegroundBrush(), null, selectorRect, 2, 2);
    }

    private void DrawCheckerboard(DrawingContext dc, Rect rect)
    {
        var lightBrush = s_checkerLightBrush;
        var darkBrush = s_checkerDarkBrush;
        var cellSize = 4;

        for (double x = rect.X; x < rect.Right; x += cellSize)
        {
            for (double y = rect.Y; y < rect.Bottom; y += cellSize)
            {
                var isLight = ((int)((x - rect.X) / cellSize) + (int)((y - rect.Y) / cellSize)) % 2 == 0;
                var cellRect = new Rect(x, y, Math.Min(cellSize, rect.Right - x), Math.Min(cellSize, rect.Bottom - y));
                dc.DrawRectangle(isLight ? lightBrush : darkBrush, null, cellRect);
            }
        }
    }

    private void DrawPreview(DrawingContext dc, Rect rect)
    {
        // Draw checkerboard for transparency
        DrawCheckerboard(dc, rect);

        // Draw current color
        var colorBrush = new SolidColorBrush(Color);
        dc.DrawRectangle(colorBrush, null, rect);

        // Draw border
        dc.DrawRectangle(null, GetBorderPen(), rect);
    }

    private Pen GetBorderPen()
    {
        var borderBrush = ResolveBorderBrush();
        var thickness = BorderThickness.Left > 0 ? BorderThickness.Left : 1;
        return new Pen(borderBrush, thickness);
    }

    private Pen GetSelectorPen()
    {
        return new Pen(ResolveForegroundBrush(), 2);
    }

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TextPrimary") as Brush
            ?? Foreground
            ?? s_whiteBrush;
    }

    private Brush ResolveBorderBrush()
    {
        if (HasLocalValue(Control.BorderBrushProperty) && BorderBrush != null)
        {
            return BorderBrush;
        }

        return TryFindResource("ControlBorder") as Brush
            ?? BorderBrush
            ?? s_grayBorderBrush;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPicker colorPicker)
        {
            // Only update internal HSV state when the Color is set externally.
            // When set internally (via spectrum/hue/alpha drag), the HSV state is already correct
            // and RGB閳墲SV conversion would lose hue information at low saturation/value.
            if (!colorPicker._isUpdatingColor)
            {
                colorPicker.UpdateFromColor((Color)e.NewValue);
            }
            colorPicker.InvalidateVisual();

            var args = new ColorChangedEventArgs(ColorChangedEvent, (Color)e.OldValue, (Color)e.NewValue);
            colorPicker.RaiseEvent(args);
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPicker colorPicker)
        {
            colorPicker.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Specifies the shape of the color spectrum in a ColorPicker.
/// </summary>
public enum ColorSpectrumShape
{
    /// <summary>
    /// Box-shaped spectrum.
    /// </summary>
    Box,

    /// <summary>
    /// Ring-shaped spectrum.
    /// </summary>
    Ring
}

/// <summary>
/// Provides data for the ColorChanged event.
/// </summary>
public sealed class ColorChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the old color.
    /// </summary>
    public Color OldColor { get; }

    /// <summary>
    /// Gets the new color.
    /// </summary>
    public Color NewColor { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorChangedEventArgs"/> class.
    /// </summary>
    public ColorChangedEventArgs(RoutedEvent routedEvent, Color oldColor, Color newColor)
    {
        RoutedEvent = routedEvent;
        OldColor = oldColor;
        NewColor = newColor;
    }
}
