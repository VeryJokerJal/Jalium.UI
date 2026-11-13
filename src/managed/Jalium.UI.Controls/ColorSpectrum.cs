using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;
/// <summary>A reusable saturation/value surface with an optional hue ring.</summary>
[TemplatePart(Name = "PART_Surface", Type = typeof(FrameworkElement))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Focused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Unfocused", GroupName = "FocusStates")]
[TemplateVisualState(Name = "Box", GroupName = "ShapeStates")]
[TemplateVisualState(Name = "Ring", GroupName = "ShapeStates")]
public class ColorSpectrum : Control
{
    public static readonly DependencyProperty HueProperty = DependencyProperty.Register(nameof(Hue), typeof(double), typeof(ColorSpectrum), new PropertyMetadata(0.0, OnColorComponentChanged, CoerceHue));
    public static readonly DependencyProperty SaturationProperty = DependencyProperty.Register(nameof(Saturation), typeof(double), typeof(ColorSpectrum), new PropertyMetadata(1.0, OnColorComponentChanged, CoerceUnit));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ColorSpectrum), new PropertyMetadata(1.0, OnColorComponentChanged, CoerceUnit));
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(nameof(Shape), typeof(ColorSpectrumShape), typeof(ColorSpectrum), new PropertyMetadata(ColorSpectrumShape.Box, OnShapeChanged));
    public static readonly DependencyProperty SpectrumSizeProperty = DependencyProperty.Register(nameof(SpectrumSize), typeof(double), typeof(ColorSpectrum), new PropertyMetadata(200.0));
    private static readonly DependencyPropertyKey SelectedColorPropertyKey = DependencyProperty.RegisterReadOnly(nameof(SelectedColor), typeof(Color), typeof(ColorSpectrum), new PropertyMetadata(Color.Red));
    public static readonly DependencyProperty SelectedColorProperty = SelectedColorPropertyKey.DependencyProperty;
    public double Hue { get => (double)GetValue(HueProperty)!; set => SetValue(HueProperty, value); }
    public double Saturation { get => (double)GetValue(SaturationProperty)!; set => SetValue(SaturationProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
    public ColorSpectrumShape Shape { get => (ColorSpectrumShape)GetValue(ShapeProperty)!; set => SetValue(ShapeProperty, value); }
    public double SpectrumSize { get => (double)GetValue(SpectrumSizeProperty)!; set => SetValue(SpectrumSizeProperty, value); }
    public Color SelectedColor => (Color)GetValue(SelectedColorProperty)!;

    public static readonly RoutedEvent ColorChangedEvent = EventManager.RegisterRoutedEvent(nameof(ColorChanged), RoutingStrategy.Bubble, typeof(EventHandler<ColorChangedEventArgs>), typeof(ColorSpectrum));
    public event EventHandler<ColorChangedEventArgs> ColorChanged { add => AddHandler(ColorChangedEvent, value); remove => RemoveHandler(ColorChangedEvent, value); }

    private FrameworkElement? _surface;
    private ColorSpectrumRegion _dragRegion;
    static ColorSpectrum() => DefaultStyleKeyProperty.OverrideMetadata(typeof(ColorSpectrum), new PropertyMetadata(typeof(ColorSpectrum)));
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => new Jalium.UI.Automation.Peers.ColorSpectrumAutomationPeer(this);
    public ColorSpectrum()
    {
        Focusable = true;
        SetCurrentValue(TransitionPropertyProperty, TransitionPropertyCollection.None());
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnPointerDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnPointerMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnPointerUp));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnSpectrumKeyDown));
        Unloaded += (_, _) => EndInput();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        EndInput();
        _surface = GetTemplateChild("PART_Surface") as FrameworkElement;
        UpdateStates();
    }

    internal override void OnTemplateContentClearing()
    {
        EndInput();
        _surface = null;
        base.OnTemplateContentClearing();
    }

    protected override void OnIsEnabledChanged(bool oldValue, bool newValue)
    {
        base.OnIsEnabledChanged(oldValue, newValue);
        if (!newValue)
            EndInput();
        UpdateStates();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        UpdateStates();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        UpdateStates();
    }

    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        _dragRegion = ColorSpectrumRegion.None;
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled || e.ChangedButton != MouseButton.Left || _surface == null)
            return;
        var point = e.GetPosition(_surface);
        var region = ColorSpectrumGeometry.Create(_surface.RenderSize, Shape).HitTest(point, Shape);
        if (region == ColorSpectrumRegion.None)
            return;
        Focus();
        if (!CaptureMouse())
            return;
        _dragRegion = region;
        UpdateFromPoint(point);
        e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!IsEnabled || _dragRegion == ColorSpectrumRegion.None || _surface == null)
            return;
        UpdateFromPoint(e.GetPosition(_surface));
        e.Handled = true;
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _dragRegion == ColorSpectrumRegion.None)
            return;
        EndInput();
        e.Handled = true;
    }

    private void UpdateFromPoint(Point point)
    {
        if (_surface == null)
            return;
        var geometry = ColorSpectrumGeometry.Create(_surface.RenderSize, Shape);
        if (_dragRegion == ColorSpectrumRegion.Hue)
        {
            double hue = Math.Atan2(point.Y - geometry.Center.Y, point.X - geometry.Center.X) * 180 / Math.PI;
            SetCurrentValue(HueProperty, hue < 0 ? hue + 360 : hue);
        }
        else if (geometry.Square.Width > 0 && geometry.Square.Height > 0)
        {
            SetCurrentValue(SaturationProperty, ColorPickerColorMath.Unit((point.X - geometry.Square.X) / geometry.Square.Width));
            SetCurrentValue(ValueProperty, ColorPickerColorMath.Unit(1 - (point.Y - geometry.Square.Y) / geometry.Square.Height));
        }
    }

    private void OnSpectrumKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsEnabled || e.Handled)
            return;
        double step = (e.KeyboardModifiers & ModifierKeys.Shift) != 0 ? .1 : .01;
        switch (e.Key)
        {
            case Key.Left:
                SetCurrentValue(SaturationProperty, Saturation - step);
                break;
            case Key.Right:
                SetCurrentValue(SaturationProperty, Saturation + step);
                break;
            case Key.Up:
                SetCurrentValue(ValueProperty, Value + step);
                break;
            case Key.Down:
                SetCurrentValue(ValueProperty, Value - step);
                break;
            case Key.PageUp:
                SetCurrentValue(HueProperty, Hue + step * 100);
                break;
            case Key.PageDown:
                SetCurrentValue(HueProperty, Hue - step * 100);
                break;
            case Key.Home:
                SetCurrentValue(SaturationProperty, 0.0);
                SetCurrentValue(ValueProperty, 1.0);
                break;
            case Key.End:
                SetCurrentValue(SaturationProperty, 1.0);
                SetCurrentValue(ValueProperty, 0.0);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void EndInput()
    {
        _dragRegion = ColorSpectrumRegion.None;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, IsKeyboardFocusWithin ? "Focused" : "Unfocused", false);
        VisualStateManager.GoToState(this, Shape == ColorSpectrumShape.Ring ? "Ring" : "Box", false);
    }

    private static object? CoerceHue(DependencyObject d, object? value) => ColorPickerColorMath.Hue((double)value!);
    private static object? CoerceUnit(DependencyObject d, object? value) => ColorPickerColorMath.Unit((double)value!);
    private static void OnColorComponentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spectrum = (ColorSpectrum)d;
        var oldColor = spectrum.SelectedColor;
        var color = ColorPickerColorMath.FromHsv(spectrum.Hue, spectrum.Saturation, spectrum.Value);
        spectrum.SetValue(SelectedColorPropertyKey, color);
        if (oldColor != color)
            spectrum.RaiseEvent(new ColorChangedEventArgs(ColorChangedEvent, oldColor, color));
    }

    private static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var spectrum = (ColorSpectrum)d;
        spectrum.EndInput();
        spectrum.UpdateStates();
    }
}
