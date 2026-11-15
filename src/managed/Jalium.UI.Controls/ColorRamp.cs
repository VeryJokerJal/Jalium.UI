using Jalium.UI.Media;

namespace Jalium.UI.Controls;
public enum ColorRampMode
{
    Hue,
    Alpha
}

/// <summary>A gradient slider reusing Slider's range, keyboard and automation behavior.</summary>
public class ColorRamp : Slider
{
    private static readonly LinearGradientBrush HueGradient = CreateHueGradient();
    private static readonly LinearGradientBrush ReversedHueGradient = CreateHueGradient(reversed: true);
    private static readonly LinearGradientBrush VerticalHueGradient = CreateHueGradient(vertical: true);
    private static readonly LinearGradientBrush ReversedVerticalHueGradient = CreateHueGradient(vertical: true, reversed: true);
    private static readonly DependencyPropertyKey RampBrushPropertyKey = DependencyProperty.RegisterReadOnly(nameof(RampBrush), typeof(Brush), typeof(ColorRamp), new PropertyMetadata(HueGradient));
    public static readonly DependencyProperty RampBrushProperty = RampBrushPropertyKey.DependencyProperty;
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(ColorRampMode), typeof(ColorRamp), new PropertyMetadata(ColorRampMode.Hue, OnRampChanged));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Color), typeof(ColorRamp), new PropertyMetadata(Color.White, OnRampChanged));
    public ColorRampMode Mode { get => (ColorRampMode)GetValue(ModeProperty)!; set => SetValue(ModeProperty, value); }
    public Color Color { get => (Color)GetValue(ColorProperty)!; set => SetValue(ColorProperty, value); }
    public Brush RampBrush => (Brush)GetValue(RampBrushProperty)!;

    private LinearGradientBrush _alphaGradient = new(Color.Transparent, Color.White, 0);
    static ColorRamp()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ColorRamp), new PropertyMetadata(typeof(ColorRamp)));
        MaximumProperty.OverrideMetadata(typeof(ColorRamp), new PropertyMetadata(360.0));
    }

    private static LinearGradientBrush CreateHueGradient(bool vertical = false, bool reversed = false)
    {
        var axis = GetGradientAxis(vertical, reversed);
        var brush = new LinearGradientBrush
        {
            StartPoint = axis.Start,
            EndPoint = axis.End
        };
        for (int index = 0; index <= 6; index++)
            brush.GradientStops.Add(new GradientStop(ColorPickerColorMath.FromHsv(index * 60, 1, 1), index / 6.0));
        brush.Freeze();
        return brush;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == OrientationProperty || e.Property == IsDirectionReversedProperty)
            UpdateRamp();
    }

    private static (Point Start, Point End) GetGradientAxis(bool vertical, bool reversed)
    {
        var start = vertical ? new Point(.5, 1) : new Point(0, .5);
        var end = vertical ? new Point(.5, 0) : new Point(1, .5);
        return reversed ? (end, start) : (start, end);
    }

    private static void OnRampChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ColorRamp)d).UpdateRamp();

    private void UpdateRamp()
    {
        bool vertical = Orientation == Orientation.Vertical;
        if (Mode == ColorRampMode.Hue)
        {
            var hueBrush = vertical
                ? (IsDirectionReversed ? ReversedVerticalHueGradient : VerticalHueGradient)
                : (IsDirectionReversed ? ReversedHueGradient : HueGradient);
            SetValue(RampBrushPropertyKey, hueBrush);
            return;
        }

        var color = Color;
        if (_alphaGradient.IsFrozen)
            _alphaGradient = new LinearGradientBrush(Color.Transparent, Color.White, 0);
        var axis = GetGradientAxis(vertical, IsDirectionReversed);
        _alphaGradient.StartPoint = axis.Start;
        _alphaGradient.EndPoint = axis.End;
        _alphaGradient.GradientStops[0].Color = Color.FromArgb(0, color.R, color.G, color.B);
        _alphaGradient.GradientStops[1].Color = Color.FromArgb(255, color.R, color.G, color.B);
        SetValue(RampBrushPropertyKey, _alphaGradient);
    }
}
